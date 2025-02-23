using Celeste.Mod.SpeedrunTool.RoomTimer;
using Celeste.Mod.SpeedrunTool.SaveLoad;

namespace Celeste.Mod.SpeedrunTool.ModInterop;
internal class CommandExport {

    [Command("srt_exportroomtimes", "export room timer data in csv format to clipboard or a file in gamePath/SRTool_RoomTimeExports folder (SpeedrunTool)")]
    public static void CmdExportRoomTimes() =>  RoomTimerManager.CmdExportRoomTimes();

    [Command("switch_slot", "Switch to another SRT save slot")]
    public static bool CmdSwitchSlot(string name) => SaveSlotsManager.SwitchSlot(name);

}
