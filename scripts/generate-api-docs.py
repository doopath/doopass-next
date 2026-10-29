#!/usr/bin/env python3
"""Generate Markdown API reference pages from the compiler's XML documentation.

The .NET compiler writes one XML file per assembly when
``GenerateDocumentationFile`` is enabled (see ``Directory.Build.props``). This
script turns those files into wiki pages:

    docs/wiki/API-Reference-Core.md
    docs/wiki/API-Reference-WebUi.md
    docs/wiki/API-Reference-Cli.md
    docs/wiki/API-Reference-Tui.md
    docs/wiki/API-Reference-Mobile.md

Usage:
    dotnet build Doopass.slnx
    dotnet build src/Doopass.Mobile/Doopass.Mobile.csproj   # not in the solution
    python scripts/generate-api-docs.py [--check]

``--check`` exits with status 1 when a page would change, which is useful in a
pre-commit hook or CI job.
"""

from __future__ import annotations

import argparse
import html
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
WIKI_DIR = REPO_ROOT / "docs" / "wiki"

# (project directory, assembly name inside the XML, wiki page name)
PAGES = [
    ("Doopass.Core", "Doopass.Core", "API-Reference-Core"),
    ("Doopass.WebUi", "Doopass.WebUi", "API-Reference-WebUi"),
    ("Doopass.Cli", "doopass", "API-Reference-Cli"),
    ("Doopass.Tui", "Doopass.Tui", "API-Reference-Tui"),
    ("Doopass.Mobile", "Doopass.Mobile", "API-Reference-Mobile"),
]

# Assemblies whose members are public because the two hosts wire them up, but
# which are not interesting to a consumer of the library.
SHORT_TYPE_NAMES = {
    "Void": "void", "String": "string", "Boolean": "bool", "Int32": "int",
    "Int64": "long", "Double": "double", "Single": "float", "Object": "object",
    "Byte": "byte", "Char": "char", "Decimal": "decimal", "Guid": "Guid",
}

MEMBER_PREFIX = {"T": "type", "M": "method", "P": "property", "F": "field", "E": "event"}


def find_docs() -> dict[str, Path]:
    """Return the newest XML doc file for every assembly we publish pages for."""
    found: dict[str, Path] = {}

    for project, assembly, _page in PAGES:
        candidates = sorted(
            (REPO_ROOT / "src" / project / "bin").glob("*/*/*.xml"),
            key=lambda path: path.stat().st_mtime,
            reverse=True,
        )

        for path in candidates:
            try:
                root = ET.parse(path).getroot()
            except ET.ParseError:
                continue

            if root.findtext("assembly/name") == assembly:
                found[assembly] = path
                break

    return found


def text_of(element: ET.Element | None) -> str:
    """Flatten a doc element to Markdown, resolving the inline XML tags we use."""
    if element is None:
        return ""

    out: list[str] = []

    def walk(node: ET.Element) -> None:
        if node.text:
            out.append(node.text)

        for child in node:
            tag = child.tag

            if tag == "see" or tag == "seealso":
                cref = child.get("cref") or ""
                langword = child.get("langword")
                if langword:
                    out.append(f"`{langword}`")
                elif cref:
                    out.append(f"`{short_name(cref)}`")
                elif child.text:
                    out.append(child.text)
            elif tag == "paramref":
                out.append(f"`{child.get('name', '')}`")
            elif tag == "typeparamref":
                out.append(f"`{child.get('name', '')}`")
            elif tag == "c":
                out.append(f"`{text_of(child)}`")
            elif tag in ("b", "strong"):
                out.append(f"**{text_of(child)}**")
            elif tag in ("i", "em"):
                out.append(f"*{text_of(child)}*")
            elif tag == "code":
                out.append(f"`{text_of(child)}`")
            elif tag == "para":
                out.append("\n\n" + text_of(child))
            elif tag == "br":
                out.append("\n")
            else:
                out.append(text_of(child))

            if child.tail:
                out.append(child.tail)

    walk(element)
    return normalise("".join(out))


def normalise(value: str) -> str:
    value = html.unescape(value)
    value = re.sub(r"[ \t]+", " ", value)
    value = re.sub(r" *\n *", "\n", value)
    return value.strip()


def inline(value: str) -> str:
    """Collapse a doc block to one line (used inside Markdown list items)."""
    return re.sub(r"\s+", " ", value).strip()


def short_name(cref: str) -> str:
    """Trim a doc-id to a readable name: T:Ns.Type -> Type, M:Ns.Type.M(...) -> Type.M(...)."""
    value = cref[2:] if len(cref) > 2 and cref[1] == ":" else cref
    value = re.sub(r"``\d+", "", value)
    value = value.replace("{", "<").replace("}", ">")

    if "(" in value:
        head, args = value.split("(", 1)
        args = args.rstrip(")")
        parts = []
        for arg in split_args(args):
            arg = arg.split(".")[-1]
            parts.append(SHORT_TYPE_NAMES.get(arg, arg))
        return f"{head.split('.')[-1]}({', '.join(parts)})"

    return value.split(".")[-1]


def split_args(value: str) -> list[str]:
    """Split a parameter list on top-level commas (generics use angle brackets)."""
    parts, depth, current = [], 0, ""
    for char in value:
        if char == "<":
            depth += 1
        elif char == ">":
            depth -= 1

        if char == "," and depth == 0:
            parts.append(current)
            current = ""
        else:
            current += char

    if current:
        parts.append(current)
    return parts


def signature(member: ET.Element, doc_id: str) -> str:
    """Human-readable member signature built from the doc id."""
    kind = doc_id[0]
    rest = doc_id[2:]

    params = member.findall("param")
    param_names = [p.get("name") for p in params if p.get("name")]

    if "(" in rest:
        head, args = rest.split("(", 1)
        arg_types = split_args(args.rstrip(")"))

        rendered = []
        for index, arg_type in enumerate(arg_types):
            arg_type = re.sub(r"``\d+", "", arg_type)
            name = param_names[index] if index < len(param_names) else ""
            if name == "ct" and arg_type.endswith("CancellationToken"):
                rendered.append("CancellationToken ct = default")
                continue
            alias = SHORT_TYPE_NAMES.get(arg_type.split(".")[-1], arg_type.split(".")[-1])
            rendered.append(f"{alias} {name}".strip())

        name = head.split(".")[-1]
        if name == "#ctor":
            type_name = head.split(".")[-2]
            name = head.split(".")[-2] if type_name else "ctor"
        return f"{name}({', '.join(rendered)})"

    if ".#ctor" in rest or rest.endswith(".#ctor"):
        return rest.split(".")[-2]

    if kind == "T":
        return rest.split(".")[-1]

    if kind == "M" and "(" not in rest and "``" not in rest:
        return rest.split(".")[-1] + "()"

    owner = rest.split(".")[-2] if "." in rest else rest
    member_name = rest.split(".")[-1]

    if kind == "P" and member_name == owner:
        return owner

    return member_name


def render_page(assembly: str, xml_path: Path) -> str:
    root = ET.parse(xml_path).getroot()

    members: dict[str, ET.Element] = {}
    for member in root.findall("members/member"):
        name = member.get("name")
        if name:
            members[name] = member

    types: dict[str, list[str]] = {doc_id[2:]: [] for doc_id in members if doc_id.startswith("T:")}

    for doc_id in members:
        if doc_id.startswith("T:"):
            continue

        candidate = ".".join(doc_id[2:].split("(")[0].split(".")[0:-1])
        while candidate and candidate not in types:
            candidate = candidate.rsplit(".", 1)[0] if "." in candidate else ""

        if candidate in types:
            types[candidate].append(doc_id)

    # Render each type first so the table of contents only lists types that
    # actually have documentation (the compiler emits entries for generated
    # types such as top-level Program).
    rendered: dict[str, list[str]] = {}

    for type_name in sorted(types):
        type_member = members[f"T:{type_name}"]
        short = type_name.split(".")[-1]

        summary = text_of(type_member.find("summary"))
        remarks = text_of(type_member.find("remarks"))

        body: list[str] = []

        for doc_id in sorted(types[type_name], key=member_sort_key):
            child = members[doc_id]
            sig = signature(child, doc_id)
            member_summary = inline(text_of(child.find("summary")))

            if not member_summary:
                continue

            body.append(f"* `{sig}` — {member_summary}")

            returns = text_of(child.find("returns"))
            if returns:
                body.append(f"  *Returns:* {inline(returns)}")

            for param in child.findall("param"):
                desc = text_of(param)
                if desc:
                    body.append(f"  * `{param.get('name')}` — {inline(desc)}")

            value = text_of(child.find("value"))
            if value:
                body.append(f"  *Value:* {inline(value)}")

            for exception in child.findall("exception"):
                desc = text_of(exception)
                cref = exception.get("cref", "")
                if desc:
                    body.append(f"  *Throws* `{short_name(cref)}`: {inline(desc)}")

        if not summary and not remarks and not body:
            continue

        block = [f"### `{short}`", ""]

        if summary:
            block.extend([summary, ""])

        if remarks:
            block.extend([remarks, ""])

        if body:
            block.extend(body)
            block.append("")
        else:
            block.extend(["*No documented members.*", ""])

        rendered[type_name] = block

    lines = [
        f"# API Reference — {assembly}",
        "",
        "> Generated from the compiler's XML documentation "
        "(`GenerateDocumentationFile=true`). Do not edit by hand: run "
        "`python scripts/generate-api-docs.py` after changing public members.",
        "",
        f"Assembly: `{assembly}` · Source: `{xml_path.relative_to(REPO_ROOT)}`",
        "",
        "## Types",
        "",
    ]

    for type_name in sorted(rendered):
        lines.append(f"* [`{type_name}`](#{anchor(type_name)})")

    lines.append("")

    namespaces: dict[str, list[str]] = {}
    for type_name in rendered:
        namespace = type_name.rsplit(".", 1)[0] if "." in type_name else "(global)"
        namespaces.setdefault(namespace, []).append(type_name)

    for namespace in sorted(namespaces):
        lines.append(f"## `{namespace}`")
        lines.append("")

        for type_name in sorted(namespaces[namespace]):
            lines.extend(rendered[type_name])

    return "\n".join(lines).rstrip() + "\n"


def member_sort_key(doc_id: str) -> tuple[int, str]:
    order = {"P": 0, "F": 1, "M": 2, "E": 3}
    return (order.get(doc_id[0], 9), doc_id)


def anchor(type_name: str) -> str:
    """GitHub anchor for a type heading: the short name, lowercased."""
    return type_name.split(".")[-1].lower()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true",
                        help="fail when a page would be rewritten")
    args = parser.parse_args()

    docs = find_docs()
    missing = [assembly for _project, assembly, _page in PAGES if assembly not in docs]
    if missing:
        print("error: no XML documentation found for: " + ", ".join(missing),
              file=sys.stderr)
        print("       build the solution (and the mobile project) first",
              file=sys.stderr)
        return 2

    changed = []

    for _project, assembly, page in PAGES:
        content = render_page(assembly, docs[assembly])
        target = WIKI_DIR / f"{page}.md"
        previous = target.read_text(encoding="utf-8") if target.exists() else None

        if previous == content:
            print(f"unchanged {target.relative_to(REPO_ROOT)}")
            continue

        changed.append(target)
        if not args.check:
            target.write_text(content, encoding="utf-8", newline="\n")
            print(f"wrote     {target.relative_to(REPO_ROOT)}")

    if args.check and changed:
        print("out of date: " + ", ".join(str(p.relative_to(REPO_ROOT)) for p in changed))
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
