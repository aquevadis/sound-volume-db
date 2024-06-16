using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Commands;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace SoundVolume;

public class ConfigSpecials : BasePluginConfig
{
    [JsonPropertyName("PluginEnabled")] public bool PluginEnabled { get; set; } = true;
    [JsonPropertyName("ForceVolOnSpawn")] public bool ForceVolOnSpawn { get; set; } = false;
    [JsonPropertyName("VolMin")] public float VolMin { get; set; } = 0.01f;
    [JsonPropertyName("VolMax")] public float VolMax { get; set; } = 1.0f;
}

public class SoundVolume : BasePlugin, IPluginConfig<ConfigSpecials>
{
    public override string ModuleName => "S1ncer3ly_MVPVol & AquaVadis";
    public override string ModuleVersion => "1.0";
    public override string ModuleAuthor => "S1ncer3ly";
    public override string ModuleDescription => "Allows players to choose their own MVP music volume settings";

    public required ConfigSpecials Config { get; set; }
    private float[] PlayerVol = new float[64];
    
    public void OnConfigParsed(ConfigSpecials config)
    {
        Config = config;
    }

    private SqliteConnection _connection = null!;
    public override void Load(bool hotReload)
    {
        AddCommand("css_vol", "Command to Set MVP Volume", cmd_vol);

        _connection = new SqliteConnection($"Data Source={Path.Join(ModuleDirectory, "Database/S1ncer3ly_MVPVol.db")}");
        _connection.Open();
        Task.Run(async () =>
        {
            await _connection.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS `S1ncer3ly_MVPVol` (
                    `steamid` UNSIGNED BIG INT NOT NULL,
                    `vol` FLOAT NOT NULL DEFAULT 0.5,
                    PRIMARY KEY (`steamid`));");
        });

        RegisterEventHandler<EventPlayerConnectFull>((@event, info) =>
        {
            if (!Config.PluginEnabled || @event.Userid == null || !@event.Userid.IsValid) return HookResult.Continue;

            var steamId = @event.Userid.AuthorizedSteamID?.SteamId64;
            var player = @event.Userid;
            Task.Run(async () =>
            {
                try
                {
                    var result = await _connection.QueryFirstOrDefaultAsync(@"SELECT `vol` FROM `S1ncer3ly_MVPVol` WHERE `steamid` = @SteamId;",
                    new
                    {
                        SteamId = steamId
                    });

                    Server.NextFrame(() =>
                    {
                        var vol = result?.vol ?? 0.5f;
                        player.ExecuteClientCommand($"snd_toolvolume {vol}");
                        player.PrintToChat($"{Localizer["Chat.Prefix"]} {Localizer["Chat.SetVol", vol]}");
                        PlayerVol[player.Slot] = vol;
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[S1ncer3ly_MVPVol] Error on PlayerConnectFull while retrieving player volume: {ex.Message}");
                    Logger.LogError($"[S1ncer3ly_MVPVol] Error on PlayerConnectFull while retrieving player volume: {ex.Message}");
                }
            });
            return HookResult.Continue;
        });

        RegisterEventHandler<EventPlayerSpawn>((@event, @info) =>
        {
            if (Config.PluginEnabled && Config.ForceVolOnSpawn && @event.Userid != null && @event.Userid.IsValid)
            {
                var player = @event.Userid;
                player.ExecuteClientCommand($"snd_toolvolume {PlayerVol[player.Slot]}");
            }
            return HookResult.Continue;
        });
    }

    private void cmd_vol(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null)
        {
            info.ReplyToCommand("[VOL] Cannot use command from RCON");
            return;
        }
        if (!Config.PluginEnabled)
        {
            player.PrintToChat($"{Localizer["Chat.Prefix"]} {ChatColors.DarkRed}Plugin is Disabled!");
            return;
        }
        var volStr = info.ArgByIndex(1);
        if (volStr == "")
        {
            player.ExecuteClientCommand("snd_toolvolume 0.5"); // Set Default Volume
            player.PrintToChat($"{Localizer["Chat.Prefix"]} {Localizer["Chat.ResetVol"]}");
            return;
        }
        if (!IsFloat(volStr))
        {
            player.PrintToChat($"{Localizer["Chat.Prefix"]} {Localizer["Chat.Invalid"]}");
            return;
        }
        var vol = Convert.ToSingle(volStr);
        if (vol < Config.VolMin)
        {
            player.PrintToChat($"{Localizer["Chat.Prefix"]} {Localizer["Chat.MinVol", Config.VolMin]}");
            return;
        }
        if (vol > Config.VolMax)
        {
            player.PrintToChat($"{Localizer["Chat.Prefix"]} {Localizer["Chat.MaxVol", Config.VolMax]}");
            return;
        }
        player.ExecuteClientCommand($"snd_toolvolume {vol}");
        player.PrintToChat($"{Localizer["Chat.Prefix"]} {Localizer["Chat.SetVol", vol]}");
        PlayerVol[player.Slot] = vol;

        var steamId = player.AuthorizedSteamID?.SteamId64;
        if (steamId == null) return;

        Task.Run(async () =>
        {
            try
            {
                await _connection.ExecuteAsync(@"
                    INSERT INTO `S1ncer3ly_MVPVol` (`steamid`, `vol`) VALUES (@SteamId, @Vol)
                    ON CONFLICT(`steamid`) DO UPDATE SET `vol` = @Vol;",
                    new
                    {
                        SteamId = steamId,
                        Vol = vol
                    });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VL_MVPVol] Error while saving player volume: {ex.Message}");
                Logger.LogError($"[VL_MVPVol] Error while saving player volume: {ex.Message}");
            }
        });
    }

    private bool IsFloat(string sVal)
    {
        return float.TryParse(sVal, out _);
    }
}
