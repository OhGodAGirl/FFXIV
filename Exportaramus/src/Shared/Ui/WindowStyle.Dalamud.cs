using Dalamud.Bindings.ImGui;

namespace Exportaramus.Shared.Ui;

internal struct WindowStyle : IDisposable
{
    private int variables;
    private int colors;
    public int VariableCount => variables;
    public int ColorCount => colors;
    public WindowStyle()
    {
        variables = 0; colors = 0;
        try
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 7); variables++;
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0); variables++;
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4); variables++;
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3); variables++;
            ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 3); variables++;
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(10)); variables++;
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new System.Numerics.Vector2(7, 4)); variables++;
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new System.Numerics.Vector2(8, 5)); variables++;
            Push(ImGuiCol.Text, VisualTokens.Primary); Push(ImGuiCol.TextDisabled, VisualTokens.Muted);
            Push(ImGuiCol.WindowBg, VisualTokens.Window); Push(ImGuiCol.ChildBg, VisualTokens.Panel);
            Push(ImGuiCol.PopupBg, VisualTokens.Raised); Push(ImGuiCol.Border, VisualTokens.Border);
            Push(ImGuiCol.BorderShadow, VisualTokens.Shadow); Push(ImGuiCol.Separator, VisualTokens.Border);
            Push(ImGuiCol.TitleBg, VisualTokens.Window); Push(ImGuiCol.TitleBgActive, VisualTokens.Raised);
            Push(ImGuiCol.TitleBgCollapsed, VisualTokens.Window); Push(ImGuiCol.FrameBg, VisualTokens.Raised);
            Push(ImGuiCol.FrameBgHovered, VisualTokens.Current.FrameHovered); Push(ImGuiCol.FrameBgActive, VisualTokens.Current.FrameActive);
            Push(ImGuiCol.Button, VisualTokens.Raised); Push(ImGuiCol.ButtonHovered, VisualTokens.Current.ButtonHovered);
            Push(ImGuiCol.ButtonActive, VisualTokens.Current.ButtonActive); Push(ImGuiCol.CheckMark, VisualTokens.Heading);
            Push(ImGuiCol.TableHeaderBg, VisualTokens.Raised); Push(ImGuiCol.TableBorderStrong, VisualTokens.Border);
            Push(ImGuiCol.TableBorderLight, VisualTokens.Current.TableBorder); Push(ImGuiCol.TableRowBg, VisualTokens.Panel);
            Push(ImGuiCol.TableRowBgAlt, VisualTokens.Alternate); Push(ImGuiCol.ScrollbarBg, VisualTokens.Current.Scrollbar);
            Push(ImGuiCol.ScrollbarGrab, VisualTokens.Current.ScrollbarGrab); Push(ImGuiCol.ScrollbarGrabHovered, VisualTokens.Border);
            Push(ImGuiCol.ScrollbarGrabActive, VisualTokens.Gold); Push(ImGuiCol.Header, VisualTokens.Current.Header);
            Push(ImGuiCol.HeaderHovered, VisualTokens.Current.HeaderHovered); Push(ImGuiCol.HeaderActive, VisualTokens.Current.HeaderActive);
            Push(ImGuiCol.TextSelectedBg, VisualTokens.Current.Selection);
        }
        catch { Dispose(); throw; }
    }
    private void Push(ImGuiCol key, System.Numerics.Vector4 value) { ImGui.PushStyleColor(key, value); colors++; }
    public void Dispose()
    {
        ImGui.PopStyleColor(colors); ImGui.PopStyleVar(variables); colors = variables = 0;
    }
}

