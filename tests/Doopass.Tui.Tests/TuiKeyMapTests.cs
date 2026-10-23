using Doopass.Tui.Ui;
using Terminal.Gui.Input;
using Xunit;

namespace Doopass.Tui.Tests;

public class TuiKeyMapTests
{
    private static readonly KeyRouteContext Plain = new(IsTextInput: false, IsList: false, CanGoBack: true);
    private static readonly KeyRouteContext InList = new(IsTextInput: false, IsList: true, CanGoBack: true);
    private static readonly KeyRouteContext InText = new(IsTextInput: true, IsList: false, CanGoBack: true);

    [Fact]
    public void Tab_cycles_forward() => Assert.Equal(TuiAction.FocusNext, TuiKeyMap.Resolve(Key.Tab, Plain));

    [Fact]
    public void ShiftTab_cycles_backward() =>
        Assert.Equal(TuiAction.FocusPrevious, TuiKeyMap.Resolve(Key.Tab.WithShift, Plain));

    [Fact]
    public void Down_and_J_move_forward_on_a_menu() =>
        Assert.Equal(TuiAction.FocusNext, TuiKeyMap.Resolve(Key.CursorDown, Plain));

    [Fact]
    public void Up_and_K_move_backward_on_a_menu() =>
        Assert.Equal(TuiAction.FocusPrevious, TuiKeyMap.Resolve(Key.CursorUp, Plain));

    [Fact]
    public void J_and_K_are_ignored_inside_a_list_so_the_list_can_use_them() =>
        Assert.Equal(TuiAction.None, TuiKeyMap.Resolve(Key.J, InList));

    [Fact]
    public void Arrows_are_ignored_inside_a_list() =>
        Assert.Equal(TuiAction.None, TuiKeyMap.Resolve(Key.CursorDown, InList));

    [Fact]
    public void Letters_are_typed_not_interpreted_while_typing() =>
        Assert.Equal(TuiAction.None, TuiKeyMap.Resolve(Key.J, InText));

    [Fact]
    public void Q_quits_but_not_while_typing() =>
        Assert.Equal(TuiAction.Quit, TuiKeyMap.Resolve(Key.Q, Plain));

    [Fact]
    public void Esc_goes_back_when_there_is_a_parent() =>
        Assert.Equal(TuiAction.Back, TuiKeyMap.Resolve(Key.Esc, Plain));

    [Fact]
    public void Esc_quits_on_the_root_screen() =>
        Assert.Equal(TuiAction.Quit, TuiKeyMap.Resolve(Key.Esc, Plain with { CanGoBack = false }));

    [Fact]
    public void H_moves_focus_back() =>
        Assert.Equal(TuiAction.FocusPrevious, TuiKeyMap.Resolve(Key.H, Plain));

    [Fact]
    public void H_leaves_a_text_pane() =>
        Assert.Equal(TuiAction.FocusPrevious, TuiKeyMap.Resolve(Key.H, InText));

    [Fact]
    public void L_moves_focus_forward() =>
        Assert.Equal(TuiAction.FocusNext, TuiKeyMap.Resolve(Key.L, Plain));

    [Fact]
    public void H_and_L_also_leave_a_list() =>
        Assert.Equal(TuiAction.FocusNext, TuiKeyMap.Resolve(Key.L, InList));

    [Fact]
    public void F1_opens_help() =>
        Assert.Equal(TuiAction.Help, TuiKeyMap.Resolve(Key.F1, Plain));
}
