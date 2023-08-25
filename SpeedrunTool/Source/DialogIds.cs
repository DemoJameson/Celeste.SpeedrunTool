namespace Celeste.Mod.SpeedrunTool;

public static class DialogIds {
    public const string Prefix = "SPEEDRUN_TOOL_";

    // From Game
    public const string KeyConfigReset = "KEY_CONFIG_RESET";
    public const string KeyConfigChanging = "KEY_CONFIG_CHANGING";
    public const string BtnConfigChanging = "BTN_CONFIG_CHANGING";
    public const string BtnConfigNoController = "BTN_CONFIG_NOCONTROLLER";

    // Common
    // ReSharper disable UnusedMember.Global
    public const string On = "SPEEDRUN_TOOL_ON";
    public const string Off = "SPEEDRUN_TOOL_OFF";
    public const string All = "SPEEDRUN_TOOL_ALL";
    public const string Chapter = "SPEEDRUN_TOOL_CHAPTER";
    public const string File = "SPEEDRUN_TOOL_FILE";
    // ReSharper restore UnusedMember.Global

    // Mod Menu
    public const string SpeedrunTool = "SPEEDRUN_TOOL";
    public const string Enabled = "SPEEDRUN_TOOL_ENABLED";
    public const string RoomTimer = "SPEEDRUN_TOOL_ROOM_TIMER";
    public const string State = "SPEEDRUN_TOOL_STATE";
    public const string DeathStatistics = "SPEEDRUN_TOOL_DEATH_STATISTICS";
    public const string MoreOptions = "SPEEDRUN_TOOL_MORE_OPTIONS";

    // RoomTimer
    public const string EndPointStyle = "SPEEDRUN_TOOL_END_POINT_STYLE";
    public const string NumberOfRooms = "SPEEDRUN_TOOL_NUMBER_OF_ROOMS";
    public const string TimeSummitFlag = "SPEEDRUN_TOOL_TIME_SUMMIT_FLAG";
    public const string TimeHeartCassette = "SPEEDRUN_TOOL_TIME_HEART_CASSETTE";
    public const string AutoTurnOffRoomTimer = "SPEEDRUN_TOOL_AUTO_TURN_OFF_ROOM_TIMER";
    public const string RoomIdEndPoint = "SPEEDRUN_TOOL_ROOM_ID_END_POINT";
    public const string DisplayRoomGold = "SPEEDRUN_TOOL_DISPLAY_ROOM_GOLD";
    public const string RoomTimerExportType = "SPEEDRUN_TOOL_ROOM_TIMER_EXPORT_TYPE";
    public const string Clipboard = "SPEEDRUN_TOOL_CLIPBOARD";

    // State
    public const string FreezeAfterLoadState = "SPEEDRUN_TOOL_FREEZE_AFTER_LOAD_STATE";
    public const string IgnoreHoldingKeys = "SPEEDRUN_TOOL_IGNORE_HOLDING_KEYS";
    public const string AutoLoadStateAfterDeath = "SPEEDRUN_TOOL_AUTO_LOAD_STATE_AFTER_DEATH";
    public const string AutoClearStateOnScreenTransition = "SPEEDRUN_TOOL_AUTO_CLEAR_STATE_ON_SCREEN_TRANSITION";
    public const string NoGcAfterLoadState = "SPEEDRUN_TOOL_NO_GC_AFTER_LOAD_STATE";
    public const string SaveTimeAndDeaths = "SPEEDRUN_TOOL_SAVE_TIME_AND_DEATHS";
    public const string SaveExtendedVariants = "SPEEDRUN_TOOL_SAVE_EXTENDED_VARIANTS";
    public const string SaveState = "SPEEDRUN_TOOL_SAVE_STATE";
    public const string LoadState = "SPEEDRUN_TOOL_LOAD_STATE";
    public const string ClearState = "SPEEDRUN_TOOL_CLEAR_STATE";
    public const string ClearStateToolTip = "SPEEDRUN_TOOL_CLEAR_STATE_TOOLTIP";
    public const string NotSavedStateTooltip = "SPEEDRUN_TOOL_NOT_SAVED_STATE_TOOLTIP";
    public const string ClearStateDialog = "SPEEDRUN_TOOL_CLEAR_STATE_DIALOG";
    public const string NotSavedStateYetDialog = "SPEEDRUN_TOOL_NOT_SAVED_STATE_YET_DIALOG";
    public const string ClearStateDialogBadeline = "SPEEDRUN_TOOL_CLEAR_STATE_DIALOG_BADELINE";
    public const string NotSavedStateYetDialogBadeline = "SPEEDRUN_TOOL_NOT_SAVED_STATE_YET_DIALOG_BADELINE";

    // End Point Style
    public const string Flag = "SPEEDRUN_TOOL_FLAG";
    public const string GoldBerry = "SPEEDRUN_TOOL_GOLD_BERRY";
    public const string Madeline = "SPEEDRUN_TOOL_MADELINE";
    public const string Badeline = "SPEEDRUN_TOOL_BADELINE";
    public const string Granny = "SPEEDRUN_TOOL_GRANNY";
    public const string Theo = "SPEEDRUN_TOOL_THEO";
    public const string Oshiro = "SPEEDRUN_TOOL_OSHIRO";
    public const string Bird = "SPEEDRUN_TOOL_BIRD";
    public const string EyeBat = "SPEEDRUN_TOOL_EYE_BAT";
    public const string Ogmo = "SPEEDRUN_TOOL_OGMO";
    public const string Skytorn = "SPEEDRUN_TOOL_SKYTORN";
    public const string Towerfall = "SPEEDRUN_TOOL_TOWERFALL";
    public const string Yuri = "SPEEDRUN_TOOL_YURI";
    public const string Random = "SPEEDRUN_TOOL_RANDOM";

    // Hotkey Config
    public const string Keyboard = "SPEEDRUN_TOOL_KEYBOARD";
    public const string Controller = "SPEEDRUN_TOOL_CONTROLLER";
    public const string ComboHotkeyDescription = "SPEEDRUN_TOOL_COMBO_HOTKEY_DESCRIPTION";
    public const string PressDeleteToClearHotkeys = "SPEEDRUN_TOOL_PRESS_DELETE_TO_CLEAR_HOTKEYS";
    public const string ToggleHotkeys = "SPEEDRUN_TOOL_TOGGLE_HOTKEYS";
    public const string OpenDebugMap = "SPEEDRUN_TOOL_OPEN_DEBUG_MAP";
    public const string ResetRoomTimerPb = "SPEEDRUN_TOOL_RESET_ROOM_TIMER_PB";
    public const string SwitchRoomTimer = "SPEEDRUN_TOOL_SWITCH_ROOM_TIMER";
    public const string IncreaseTimedRooms = "SPEEDRUN_TOOL_INCREASE_TIMED_ROOMS";
    public const string DecreaseTimedRooms = "SPEEDRUN_TOOL_DECREASE_TIMED_ROOMS";
    public const string SetEndPoint = "SPEEDRUN_TOOL_SET_END_POINT";
    public const string SetAdditionalEndPoint = "SPEEDRUN_TOOL_SET_ADDITIONAL_END_POINT";
    public const string SetRoomIdEndPoint = "SPEEDRUN_TOOL_SET_ROOM_ID_END_POINT";
    public const string ClearRoomIdEndPoint = "SPEEDRUN_TOOL_CLEAR_ROOM_ID_END_POINT";
    public const string ResetRoomTimerPbTooltip = "SPEEDRUN_TOOL_RESET_ROOM_TIMER_PB_TOOLTIP";
    public const string ResetRoomTimerPbDialog = "SPEEDRUN_TOOL_RESET_ROOM_TIMER_PB_DIALOG";
    public const string TeleportToPreviousRoom = "SPEEDRUN_TOOL_TELEPORT_TO_PREVIOUS_ROOM";
    public const string TeleportToNextRoom = "SPEEDRUN_TOOL_TELEPORT_TO_NEXT_ROOM";
    public const string AlreadyFirstRoomTooltip = "SPEEDRUN_TOOL_ALREADY_FIRST_ROOM_TOOLTIP";
    public const string AlreadyFirstRoomDialog = "SPEEDRUN_TOOL_ALREADY_FIRST_ROOM_DIALOG";
    public const string AlreadyFirstRoomDialogBadeline = "SPEEDRUN_TOOL_ALREADY_FIRST_ROOM_DIALOG_BADELINE";
    public const string AlreadyLastRoomTooltip = "SPEEDRUN_TOOL_ALREADY_LAST_ROOM_TOOLTIP";
    public const string AlreadyLastRoomDialog = "SPEEDRUN_TOOL_ALREADY_LAST_ROOM_DIALOG";
    public const string AlreadyLastRoomDialogBadeline = "SPEEDRUN_TOOL_ALREADY_LAST_ROOM_DIALOG_BADELINE";
    public const string SwitchAutoLoadState = "SPEEDRUN_TOOL_SWITCH_AUTO_LOAD_STATE";
    public const string SpawnTowerViewer = "SPEEDRUN_TOOL_SPAWN_TOWER_VIEWER";
    public const string ToggleFullscreen = "SPEEDRUN_TOOL_TOGGLE_FULLSCREEN";
    public const string ExportRoomTimes = "SPEEDRUN_TOOL_EXPORT_ROOM_TIMES";
    public const string ExportRoomTimesSuccess = "SPEEDRUN_TOOL_EXPORT_ROOM_TIMES_SUCCESS";
    public const string ExportRoomTimesFail = "SPEEDRUN_TOOL_EXPORT_ROOM_TIMES_FAIL";

    // Death Statistics
    public const string MaxNumberOfDeathData = "SPEEDRUN_TOOL_MAX_NUMBER_OF_DEATH_DATA";
    public const string CheckDeathStatistics = "SPEEDRUN_TOOL_CHECK_DEATH_STATISTICS";
    public const string DeathStatisticsHeader = "SPEEDRUN_TOOL_DEATH_STATISTICS_HEADER";
    public const string DeathStatisticsHeaderDebug = "SPEEDRUN_TOOL_DEATH_STATISTICS_HEADER_DEBUG";
    public const string ClearDeathStatistics = "SPEEDRUN_TOOL_CLEAR_DEATH_STATISTICS";
    public const string Room = "SPEEDRUN_TOOL_ROOM";
    public const string LostTime = "SPEEDRUN_TOOL_LOST_TIME";
    public const string CauseOfDeath = "SPEEDRUN_TOOL_CAUSE_OF_DEATH";
    public const string TotalDeathCount = "SPEEDRUN_TOOL_TOTAL_DEATH_COUNT";
    public const string TotalLostTime = "SPEEDRUN_TOOL_TOTAL_LOST_TIME";
    public const string NoData = "SPEEDRUN_TOOL_NO_DATA";

    // More Options
    public const string TeleportRoomCategory = "SPEEDRUN_TOOL_TELEPORT_ROOM_CATEGORY";
    public const string Default = "SPEEDRUN_TOOL_DEFAULT";
    public const string Any = "SPEEDRUN_TOOL_ANY";
    public const string RespawnSpeed = "SPEEDRUN_TOOL_RESPAWN_SPEED";
    public const string RestartChapterSpeed = "SPEEDRUN_TOOL_RESTART_CHAPTER_SPEED";
    public const string SkipRestartChapterScreenWipe = "SPEEDRUN_TOOL_SKIP_RESTART_CHAPTER_SCREEN_WIPE";
    public const string AllowPauseDuringDeath = "SPEEDRUN_TOOL_ALLOW_PAUSE_DURING_DEATH";
    public const string MuteInBackground = "SPEEDRUN_TOOL_MUTE_IN_BACKGROUND";
    public const string FixCoreRefillDashAfterTeleport = "SPEEDRUN_TOOL_FIX_CORE_REFILL_DASH_AFTER_TELEPORT";
    public const string PopupMessageStyle = "SPEEDRUN_TOOL_POPUP_MESSAGE_STYLE";
    public const string OptionState = "SPEEDRUN_TOOL_OPTION_STATE";
    public const string EnableTimerOnAreaComplete = "SPEEDRUN_TOOL_ENABLE_TIMER_ON_AREA_COMPLETE";
    public const string Hotkeys = "SPEEDRUN_TOOL_HOTKEYS";
    public const string HotkeysConfig = "SPEEDRUN_TOOL_HOTKEYS_CONFIG";
    public const string UnlockCamera = "SPEEDRUN_TOOL_UNLOCK_CAMERA";
}