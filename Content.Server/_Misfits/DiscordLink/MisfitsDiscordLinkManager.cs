using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Content.Server._NC.CCCvars;
using Content.Server.Database;
using Content.Server._Misfits.Supporter;
using Content.Shared.CCVar;
using Content.Shared._Misfits.DiscordLink;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._Misfits.DiscordLink;

public sealed class MisfitsDiscordLinkManager
{
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IServerNetManager _net = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly ISupporterManager _supporters = default!;
    [Dependency] private readonly IResourceManager _res = default!;

    private static readonly ResPath SupporterRoleMappingsPath = new("/_Misfits/Supporter/discord_role_mappings.json");
    private readonly HttpClient _http = new();
    private ISawmill _sawmill = default!;
    private string _apiUrl = string.Empty;
    private string _misfitsApiKey = string.Empty;
    private string _legacyApiKey = string.Empty;
    private string _activeApiKeySource = "none";
    private List<DiscordSupporterRoleMapping> _roleMappings = [];

    public void Initialize()
    {
        IoCManager.InjectDependencies(this);
        _sawmill = Logger.GetSawmill("misfits.discordLink");

        _cfg.OnValueChanged(CCVars.MisfitsDiscordLinkApiUrl, value => _apiUrl = value.TrimEnd('/'), true);
        _cfg.OnValueChanged(CCVars.MisfitsDiscordLinkApiKey, value =>
        {
            _misfitsApiKey = value.Trim();
            UpdateAuthorizationHeader();
        }, true);
        _cfg.OnValueChanged(CCCVars.ApiKey, value =>
        {
            _legacyApiKey = value.Trim();
            UpdateAuthorizationHeader();
        }, true);
        if (!TryLoadRoleMappingsFromFile())
            _cfg.OnValueChanged(CCVars.MisfitsSupporterDiscordRoleMappings, OnRoleMappingsChanged, true);

        _net.RegisterNetMessage<MsgMisfitsDiscordLinkStatusRequest>(OnStatusRequest);
        _net.RegisterNetMessage<MsgMisfitsDiscordLinkBegin>(OnBegin);
        _net.RegisterNetMessage<MsgMisfitsDiscordLinkCheck>(OnCheck);
        _net.RegisterNetMessage<MsgMisfitsDiscordLinkStatus>();

        _player.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    private void UpdateAuthorizationHeader()
    {
        var key = string.IsNullOrWhiteSpace(_misfitsApiKey)
            ? _legacyApiKey
            : _misfitsApiKey;

        _activeApiKeySource = string.IsNullOrWhiteSpace(_misfitsApiKey)
            ? string.IsNullOrWhiteSpace(_legacyApiKey) ? "none" : "jerry.discord_apikey"
            : "misfits.discord_link_api_key";

        _http.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(key)
            ? null
            : new AuthenticationHeaderValue("Bearer", key);
    }

    private async void OnStatusRequest(MsgMisfitsDiscordLinkStatusRequest msg)
    {
        await SendStatus(msg.MsgChannel.UserId, checkRemote: false);
    }

    private async void OnBegin(MsgMisfitsDiscordLinkBegin msg)
    {
        var userId = msg.MsgChannel.UserId;
        if (await IsLocallyLinked(userId))
        {
            await SyncSupporterFromDiscordRoles(userId);
            Send(userId, linked: true);
            return;
        }

        var link = await GenerateLink(userId);
        Send(userId, linked: false, link: link ?? string.Empty, error: link == null ? "Unable to create Discord link." : string.Empty);
    }

    private async void OnCheck(MsgMisfitsDiscordLinkCheck msg)
    {
        await SendStatus(msg.MsgChannel.UserId, checkRemote: true);
    }

    private async Task SendStatus(NetUserId userId, bool checkRemote)
    {
        if (await IsLocallyLinked(userId))
        {
            await SyncSupporterFromDiscordRoles(userId);
            Send(userId, linked: true);
            return;
        }

        if (!checkRemote)
        {
            Send(userId, linked: false);
            return;
        }

        var discordId = await GetVerifiedDiscordId(userId);
        if (discordId == null)
        {
            Send(userId, linked: false);
            return;
        }

        try
        {
            await _db.SetPlayerDiscordIdAsync(userId, discordId);
            await SyncSupporterFromDiscordRoles(userId);
            Send(userId, linked: true);
        }
        catch (Exception ex)
        {
            _sawmill.Warning($"Failed to save Discord link for {userId}: {ex}");
            Send(userId, linked: false, error: "That Discord account is already linked to another account.");
        }
    }

    private async Task<bool> IsLocallyLinked(NetUserId userId)
    {
        var discordId = await _db.GetPlayerDiscordIdAsync(userId);
        return !string.IsNullOrWhiteSpace(discordId);
    }

    private async void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus is not (SessionStatus.Connected or SessionStatus.InGame))
            return;

        try
        {
            await SyncSupporterFromDiscordRolesIfLinked(args.Session.UserId, args.Session.Name);
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Failed to sync Discord supporter roles for {args.Session.Name} ({args.Session.UserId}): {ex}");
        }
    }

    private async Task SyncSupporterFromDiscordRolesIfLinked(NetUserId userId, string? username = null)
    {
        if (!await IsLocallyLinked(userId))
            return;

        _sawmill.Debug($"Checking Discord supporter roles for {username ?? userId.ToString()} ({userId}).");
        await SyncSupporterFromDiscordRoles(userId, username);
    }

    private async Task<string?> GenerateLink(NetUserId userId)
    {
        if (string.IsNullOrWhiteSpace(_apiUrl))
            return null;

        try
        {
            var response = await _http.GetAsync($"{_apiUrl}/link?uid={userId}");
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _sawmill.Warning($"Discord link service returned {(int) response.StatusCode} for {userId}; auth={GetAuthDiagnostic()}: {TrimForLog(content)}");
                return null;
            }

            return ParseLinkResponse(content);
        }
        catch (Exception ex)
        {
            _sawmill.Warning($"Failed to generate Discord link for {userId}: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> GetVerifiedDiscordId(NetUserId userId)
    {
        if (string.IsNullOrWhiteSpace(_apiUrl))
            return null;

        try
        {
            var response = await _http.GetAsync($"{_apiUrl}/uuid?method=uid&id={userId}");
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _sawmill.Debug($"Discord verify service returned {(int) response.StatusCode} for {userId}; auth={GetAuthDiagnostic()}: {TrimForLog(content)}");
                return null;
            }

            var body = JsonSerializer.Deserialize<DiscordUuidResponse>(content);
            return string.IsNullOrWhiteSpace(body?.DiscordId) ? null : body.DiscordId;
        }
        catch (Exception ex)
        {
            _sawmill.Warning($"Failed to verify Discord link for {userId}: {ex.Message}");
            return null;
        }
    }

    private async Task<List<string>?> GetRoles(NetUserId userId)
    {
        if (string.IsNullOrWhiteSpace(_apiUrl))
            return null;

        try
        {
            var response = await _http.GetAsync($"{_apiUrl}/roles?method=uid&id={userId}");
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var level = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? LogLevel.Warning
                    : LogLevel.Debug;
                _sawmill.Log(level, $"Discord role service returned {(int) response.StatusCode} for {userId}; auth={GetAuthDiagnostic()}: {TrimForLog(content)}");
                return null;
            }

            var body = JsonSerializer.Deserialize<RolesResponse>(content);
            return body?.Roles?.ToList();
        }
        catch (Exception ex)
        {
            _sawmill.Warning($"Failed to get Discord roles for {userId}: {ex.Message}");
            return null;
        }
    }

    private async Task SyncSupporterFromDiscordRoles(NetUserId userId, string? username = null)
    {
        if (_roleMappings.Count == 0)
            return;

        var roles = await GetRoles(userId);
        if (roles == null)
            return;

        var roleSet = roles.ToHashSet(StringComparer.Ordinal);
        var match = _roleMappings
            .Where(mapping => roleSet.Contains(mapping.Role))
            .OrderByDescending(mapping => mapping.Priority)
            .FirstOrDefault();

        if (match == null)
        {
            if (_supporters.TryGetSupporter(userId, out var existing))
            {
                await _supporters.RemoveSupporterAsync(userId.UserId);
                _sawmill.Info($"Removed Discord supporter data for {existing.Username} ({userId}); no configured Discord supporter role found.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(username) &&
            _player.TryGetSessionById(userId, out var session))
        {
            username = session.Name;
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            var record = await _db.GetPlayerRecordByUserId(userId);
            username = record?.LastSeenUserName ?? userId.ToString();
        }

        await _supporters.SetSupporterAsync(userId.UserId, username, match.Title, match.Color);
        _sawmill.Info($"Synced Discord supporter data for {username} ({userId}) from role {match.Role} with priority {match.Priority}.");
    }

    private void Send(NetUserId userId, bool linked, string link = "", string error = "")
    {
        if (!_player.TryGetSessionById(userId, out var session))
            return;

        _net.ServerSendMessage(new MsgMisfitsDiscordLinkStatus
        {
            IsLinked = linked,
            Link = link,
            Error = error,
        }, session.Channel);
    }

    private static string? ParseLinkResponse(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.Trim('"');
        }

        var body = JsonSerializer.Deserialize<DiscordLinkResponse>(content);
        return string.IsNullOrWhiteSpace(body?.Link) ? null : body.Link;
    }

    private static string TrimForLog(string content)
    {
        content = content.ReplaceLineEndings(" ").Trim();
        return content.Length <= 300 ? content : content[..300];
    }

    private string GetAuthDiagnostic()
    {
        var parameter = _http.DefaultRequestHeaders.Authorization?.Parameter;
        if (string.IsNullOrEmpty(parameter))
            return "none";

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(parameter));
        return $"{_activeApiKeySource}, len={parameter.Length}, sha256={Convert.ToHexString(hash)[..12]}";
    }

    private void OnRoleMappingsChanged(string value)
    {
        LoadRoleMappings(value, CCVars.MisfitsSupporterDiscordRoleMappings.Name);
    }

    private bool TryLoadRoleMappingsFromFile()
    {
        if (!_res.TryContentFileRead(SupporterRoleMappingsPath, out var file))
            return false;

        using var reader = new StreamReader(file);
        LoadRoleMappings(reader.ReadToEnd(), SupporterRoleMappingsPath.ToString());
        return true;
    }

    private void LoadRoleMappings(string value, string source)
    {
        try
        {
            var mappings = JsonSerializer.Deserialize<List<DiscordSupporterRoleMapping>>(value,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

            _roleMappings = mappings
                .Where(mapping => !string.IsNullOrWhiteSpace(mapping.Role))
                .Select(mapping => mapping with
                {
                    Role = mapping.Role.Trim(),
                    Title = string.IsNullOrWhiteSpace(mapping.Title) ? null : mapping.Title.Trim(),
                    Color = string.IsNullOrWhiteSpace(mapping.Color) ? null : mapping.Color.Trim(),
                })
                .ToList();

            _sawmill.Info($"Loaded {_roleMappings.Count} Discord supporter role mapping(s) from {source}.");
        }
        catch (Exception ex)
        {
            _roleMappings = [];
            _sawmill.Error($"Failed to parse Discord supporter role mappings from {source}: {ex.Message}");
        }
    }

    private sealed class DiscordUuidResponse
    {
        [JsonPropertyName("discord_id")]
        public string DiscordId { get; set; } = string.Empty;
    }

    private sealed class DiscordLinkResponse
    {
        [JsonPropertyName("link")]
        public string Link { get; set; } = string.Empty;
    }

    private sealed class RolesResponse
    {
        [JsonPropertyName("roles")]
        public string[] Roles { get; set; } = [];
    }

    private sealed record DiscordSupporterRoleMapping
    {
        [JsonPropertyName("role")]
        public string Role { get; init; } = string.Empty;

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("color")]
        public string? Color { get; init; }

        [JsonPropertyName("priority")]
        public int Priority { get; init; }
    }
}
