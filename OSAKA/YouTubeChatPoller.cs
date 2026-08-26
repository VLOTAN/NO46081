using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace OSAKA;

public class YouTubeChatPoller
{
    private static readonly HttpClient _httpClient = new();
    private bool _isRunning;
    private readonly HashSet<string> _processedMessageIds = new();
    private readonly Queue<string> _messageIdQueue = new();

    // NOTE: これはスクレイピングによる簡易的なアプローチです。
    // APIキーやOAuthが不要ですが、YouTubeの仕様変更で動かなくなる可能性があります。
    public async Task StartPollingAsync(string url, CancellationToken ct)
    {
        _isRunning = true;

        try
        {
            var videoId = ExtractVideoId(url);
            if (videoId == null)
                return;

            // 配信予約中は live chat の continuation がまだ発行されていない
            // ことがあるため、取得できるまで終了せず再取得する。
            while (_isRunning && !ct.IsCancellationRequested)
            {
                string html = string.Empty;
                Match apiKeyMatch = Match.Empty;
                Match clientVersionMatch = Match.Empty;
                Match continuationMatch = Match.Empty;

                try
                {
                    html = await _httpClient.GetStringAsync(url, ct);

                    apiKeyMatch = Regex.Match(
                        html,
                        @"[""']INNERTUBE_API_KEY[""']\s*:\s*[""']([^""']+)[""']");

                    clientVersionMatch = Regex.Match(
                        html,
                        @"[""']clientVersion[""']\s*:\s*[""']([^""']+)[""']");

                    continuationMatch = Regex.Match(
                        html,
                        @"""continuation"":\s*""([^""]+)""");

                    if (!apiKeyMatch.Success || !continuationMatch.Success)
                    {
                        var liveChatUrl =
                            $"https://www.youtube.com/live_chat?is_popout=1&v={videoId}";

                        html = await _httpClient.GetStringAsync(liveChatUrl, ct);

                        apiKeyMatch = Regex.Match(
                            html,
                            @"[""']INNERTUBE_API_KEY[""']\s*:\s*[""']([^""']+)[""']");

                        clientVersionMatch = Regex.Match(
                            html,
                            @"[""']clientVersion[""']\s*:\s*[""']([^""']+)[""']");

                        continuationMatch = Regex.Match(
                            html,
                            @"""continuation"":\s*""([^""]+)""");

                        if (!continuationMatch.Success)
                        {
                            continuationMatch = Regex.Match(
                                html,
                                @"[""']continuation[""']\s*:\s*[""']([^""']+)");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"YouTube chat initialization failed: {ex.Message}");
                }

                if (!apiKeyMatch.Success || !continuationMatch.Success)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "YouTube live chat continuation not available. Retrying in 5 seconds...");

                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                    continue;
                }

                string apiKey = apiKeyMatch.Groups[1].Value;
                string clientVersion = clientVersionMatch.Success
                    ? clientVersionMatch.Groups[1].Value
                    : "2.20260101.00.00";
                string continuation = continuationMatch.Groups[1].Value;

                bool restartInitialization = false;

                while (_isRunning && !ct.IsCancellationRequested)
                {
                    try
                    {
                        var apiUrl =
                            $"https://www.youtube.com/youtubei/v1/live_chat/get_live_chat?key={apiKey}";

                        var payload = new
                        {
                            context = new
                            {
                                client = new
                                {
                                    clientName = "WEB",
                                    clientVersion = clientVersion
                                }
                            },
                            continuation = continuation
                        };

                        using var content = new StringContent(
                            JsonSerializer.Serialize(payload),
                            System.Text.Encoding.UTF8,
                            "application/json");

                        var response = await _httpClient.PostAsync(apiUrl, content, ct);

                        if (!response.IsSuccessStatusCode)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"YouTube live chat HTTP error: {(int)response.StatusCode} {response.ReasonPhrase}");
                            restartInitialization = true;
                            break;
                        }

                        var jsonResponse =
                            await response.Content.ReadAsStringAsync(ct);

                        using var doc = JsonDocument.Parse(jsonResponse);
                        var root = doc.RootElement;

                        if (!root.TryGetProperty("continuationContents", out var continuationContents) ||
                            !continuationContents.TryGetProperty(
                                "liveChatContinuation",
                                out var conContents))
                        {
                            System.Diagnostics.Debug.WriteLine(
                                "YouTube live chat continuation became invalid. Reinitializing...");
                            restartInitialization = true;
                            break;
                        }

                        if (conContents.TryGetProperty("continuations", out var continuations) &&
                            continuations.GetArrayLength() > 0)
                        {
                            var contData = continuations[0];

                            if (contData.TryGetProperty(
                                    "invalidationContinuationData",
                                    out var invData))
                            {
                                continuation =
                                    invData.GetProperty("continuation").GetString()
                                    ?? continuation;
                            }
                            else if (contData.TryGetProperty(
                                         "timedContinuationData",
                                         out var timedData))
                            {
                                continuation =
                                    timedData.GetProperty("continuation").GetString()
                                    ?? continuation;
                            }
                            else
                            {
                                restartInitialization = true;
                                break;
                            }
                        }
                        else
                        {
                            restartInitialization = true;
                            break;
                        }

                        if (conContents.TryGetProperty("actions", out var actions))
                        {
                            foreach (var action in actions.EnumerateArray())
                            {
                                if (!action.TryGetProperty("addChatItemAction", out var addChat))
                                    continue;

                                var item = addChat.GetProperty("item");

                                // ギフト通知はYouTube側のレスポンスによって
                                // rendererの名前・階層が変わるため、通常の
                                // liveChatMembershipItemRendererより先に判定する。
                                if (TryGetGiftAnnouncementRenderer(item, out var genericGiftRenderer))
                                {
                                    ProcessGiftPurchaseRenderer(
                                        genericGiftRenderer,
                                        _processedMessageIds,
                                        _messageIdQueue);
                                }
                                else if (item.TryGetProperty(
                                             "liveChatTextMessageRenderer",
                                             out var renderer))
                                {
                                    ProcessRenderer(
                                        renderer,
                                        _processedMessageIds,
                                        _messageIdQueue);
                                }
                                else if (item.TryGetProperty(
                                             "liveChatPaidMessageRenderer",
                                             out var paidRenderer))
                                {
                                    ProcessRenderer(
                                        paidRenderer,
                                        _processedMessageIds,
                                        _messageIdQueue);
                                }
                                else if (item.TryGetProperty(
                                             "liveChatMembershipItemRenderer",
                                             out var memberRenderer))
                                {
                                    ProcessRenderer(
                                        memberRenderer,
                                        _processedMessageIds,
                                        _messageIdQueue,
                                        true);
                                }
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"YouTube live chat JSON error: {ex.Message}");
                        restartInitialization = true;
                        break;
                    }
                    catch (KeyNotFoundException ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"YouTube live chat response format error: {ex.Message}");
                        restartInitialization = true;
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Error during chat polling: {ex.Message}");
                        await Task.Delay(1000, ct);
                    }

                    await Task.Delay(300, ct);
                }

                if (!restartInitialization)
                    break;

                await Task.Delay(1000, ct);
            }
        }
        catch (TaskCanceledException)
        {
        }
        finally
        {
            _isRunning = false;
        }
    }

    private enum MembershipEventType
    {
        None,
        NewMember,
        GiftPurchase,
        GiftReceived
    }

    private static bool TryGetGiftAnnouncementRenderer(JsonElement item, out JsonElement giftRenderer)
    {
        giftRenderer = default;

        // 送った側の購入通知だけを対象にする。
        // 受け取った側にもmembership/gift系rendererが出るため、
        // 名前に「MembershipGift」が含まれるだけでは判定しない。
        return TryFindGiftPurchaseAnnouncementRecursive(item, out giftRenderer);
    }

    private static bool TryFindGiftPurchaseAnnouncementRecursive(
        JsonElement element,
        out JsonElement renderer)
    {
        renderer = default;

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                string name = property.Name;

                // 「ギフトを送った側」の購入通知だけを拾う。
                // liveChatMembershipItemRenderer は加入通知/受取通知にも使われるため、
                // MembershipGift という文字だけでは判定しない。
                bool isGiftPurchaseRenderer =
                    name.Equals(
                        "liveChatSponsorshipsGiftPurchaseAnnouncementRenderer",
                        StringComparison.OrdinalIgnoreCase) ||
                    (name.Contains("Sponsorships", StringComparison.OrdinalIgnoreCase) &&
                     name.Contains("GiftPurchase", StringComparison.OrdinalIgnoreCase) &&
                     name.Contains("AnnouncementRenderer", StringComparison.OrdinalIgnoreCase));

                if (isGiftPurchaseRenderer)
                {
                    renderer = property.Value;
                    return true;
                }

                if (property.Value.ValueKind == JsonValueKind.Object &&
                    TryFindGiftPurchaseAnnouncementRecursive(property.Value, out renderer))
                    return true;

                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var child in property.Value.EnumerateArray())
                    {
                        if (TryFindGiftPurchaseAnnouncementRecursive(child, out renderer))
                            return true;
                    }
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                if (TryFindGiftPurchaseAnnouncementRecursive(child, out renderer))
                    return true;
            }
        }

        return false;
    }

    private static string GetSimpleText(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return string.Empty;

        if (element.TryGetProperty("simpleText", out var simpleText) &&
            simpleText.ValueKind == JsonValueKind.String)
        {
            return simpleText.GetString() ?? string.Empty;
        }

        if (element.TryGetProperty("runs", out var runs) &&
            runs.ValueKind == JsonValueKind.Array)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var run in runs.EnumerateArray())
            {
                if (run.TryGetProperty("text", out var text) &&
                    text.ValueKind == JsonValueKind.String)
                {
                    sb.Append(text.GetString());
                }
            }
            return sb.ToString();
        }

        return string.Empty;
    }

    private static bool TryFindTextPropertyRecursive(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = string.Empty;

        if (element.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = GetSimpleText(property.Value);
                if (!string.IsNullOrWhiteSpace(value))
                    return true;
            }

            if (property.Value.ValueKind == JsonValueKind.Object &&
                TryFindTextPropertyRecursive(property.Value, propertyName, out value))
            {
                return true;
            }

            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in property.Value.EnumerateArray())
                {
                    if (TryFindTextPropertyRecursive(child, propertyName, out value))
                        return true;
                }
            }
        }

        return false;
    }

    private static bool TryFindGiftCountRecursive(JsonElement element, out int giftCount)
    {
        giftCount = 0;

        if (element.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals("giftMembershipsCount", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind == JsonValueKind.Number &&
                    property.Value.TryGetInt32(out giftCount) && giftCount > 0)
                {
                    return true;
                }

                if (property.Value.ValueKind == JsonValueKind.String &&
                    int.TryParse(property.Value.GetString(), out giftCount) && giftCount > 0)
                {
                    return true;
                }
            }

            if (property.Value.ValueKind == JsonValueKind.Object &&
                TryFindGiftCountRecursive(property.Value, out giftCount))
            {
                return true;
            }

            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in property.Value.EnumerateArray())
                {
                    if (TryFindGiftCountRecursive(child, out giftCount))
                        return true;
                }
            }
        }

        return false;
    }

    private static string CollectTextRecursive(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
            return element.GetString() ?? string.Empty;

        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("simpleText", out var simpleText) &&
                simpleText.ValueKind == JsonValueKind.String)
                return simpleText.GetString() ?? string.Empty;

            var sb = new System.Text.StringBuilder();
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals("text", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("simpleText", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("message", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("headerSubtext", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("primaryText", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("subtext", StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append(CollectTextRecursive(property.Value));
                }
                else if (property.Value.ValueKind == JsonValueKind.Object ||
                         property.Value.ValueKind == JsonValueKind.Array)
                {
                    sb.Append(CollectTextRecursive(property.Value));
                }
            }
            return sb.ToString();
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var child in element.EnumerateArray())
                sb.Append(CollectTextRecursive(child));
            return sb.ToString();
        }

        return string.Empty;
    }

    private static bool TryExtractGiftCountFromPrimaryText(
        JsonElement giftRenderer,
        out int giftCount)
    {
        giftCount = 0;

        if (!giftRenderer.TryGetProperty("header", out var header) ||
            !header.TryGetProperty("liveChatSponsorshipsHeaderRenderer", out var sponsorshipHeader) ||
            !sponsorshipHeader.TryGetProperty("primaryText", out var primaryText) ||
            !primaryText.TryGetProperty("runs", out var runs) ||
            runs.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        bool foundSent = false;

        foreach (var run in runs.EnumerateArray())
        {
            if (!run.TryGetProperty("text", out var textElement) ||
                textElement.ValueKind != JsonValueKind.String)
                continue;

            string runText = textElement.GetString() ?? string.Empty;

            if (!foundSent)
            {
                if (runText.Trim().Equals("Sent", StringComparison.OrdinalIgnoreCase))
                    foundSent = true;
                continue;
            }

            if (int.TryParse(runText.Trim().Replace(",", ""),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsed) && parsed > 0)
            {
                giftCount = parsed;
                return true;
            }

            break;
        }

        // runの分割形式が変わった場合の予備処理。
        var allText = new System.Text.StringBuilder();
        foreach (var run in runs.EnumerateArray())
        {
            if (run.TryGetProperty("text", out var textElement) &&
                textElement.ValueKind == JsonValueKind.String)
                allText.Append(textElement.GetString());
        }

        var match = Regex.Match(
            allText.ToString(),
            @"\bSent\s+(\d[\d,]*)\b",
            RegexOptions.IgnoreCase);

        if (match.Success &&
            int.TryParse(match.Groups[1].Value.Replace(",", ""),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var fallbackCount) && fallbackCount > 0)
        {
            giftCount = fallbackCount;
            return true;
        }

        return false;
    }

    private void ProcessGiftPurchaseRenderer(
        JsonElement giftRenderer,
        HashSet<string> processedIds,
        Queue<string> idQueue)
    {
        string author = string.Empty;
        int giftCount = 0;
        string messageId = string.Empty;

        // ギフト購入者（送った側）のauthorNameだけを取得する。
        TryFindTextPropertyRecursive(giftRenderer, "authorName", out author);

        // 購入通知に含まれるgiftMembershipsCountを取得。
        TryFindGiftCountRecursive(giftRenderer, out giftCount);

        // 現在のYouTube形式では primaryText.runs が
        // "Sent " / "1" / " " / "Marine Ch. 宝鐘マリン" / " gift memberships"
        // のように分割される。人数は「Sent の直後のrun」から最優先で取得する。
        if (giftCount <= 0)
            TryExtractGiftCountFromPrimaryText(giftRenderer, out giftCount);

        if (giftRenderer.TryGetProperty("id", out var idElement) &&
            idElement.ValueKind == JsonValueKind.String)
        {
            messageId = idElement.GetString() ?? string.Empty;
        }

        string visibleText = CollectTextRecursive(giftRenderer);

        // 購入通知に人数が明示されていない形式の予備処理。
        if (giftCount <= 0 && !string.IsNullOrWhiteSpace(visibleText))
        {
            // 「Sent 1 Marine Ch. 宝鐘マリン gift memberships」のように、
            // 人数と "gift memberships" の間にチャンネル名が入る。
            var match = Regex.Match(
                visibleText,
                @"\bSent\s+(\d[\d,]*)\b.*?\bgift\s+memberships?\b",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (match.Success)
                int.TryParse(match.Groups[1].Value.Replace(",", ""), out giftCount);
        }

        // 重要:
        // 「ギフトを受け取りました」側の通知は、同じgift系rendererを
        // 持っていても送信者側の購入通知ではない場合がある。
        // 受取通知をここで弾く。
        if (IsGiftRecipientNotification(giftRenderer, visibleText))
        {
            System.Diagnostics.Debug.WriteLine(
                $"[YouTubeChatPoller] メンバーシップギフト受取通知を無視: " +
                $"author='{author}', text='{visibleText}'");
            return;
        }

        if (string.IsNullOrWhiteSpace(author) || giftCount <= 0)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[YouTubeChatPoller] メンバーシップギフト送信者取得失敗: " +
                $"author='{author}', count={giftCount}, text='{visibleText}', renderer={giftRenderer}");
            return;
        }

        string msgKey = string.IsNullOrWhiteSpace(messageId)
            ? $"{author}:membershipGift:{giftCount}:{visibleText}"
            : messageId;

        if (processedIds.Contains(msgKey))
            return;

        processedIds.Add(msgKey);
        idQueue.Enqueue(msgKey);

        if (idQueue.Count > 200)
        {
            var oldKey = idQueue.Dequeue();
            processedIds.Remove(oldKey);
        }

        string messageText =
            $"{author} がメンバーシップギフトを {giftCount} 人に贈りました";

        System.Diagnostics.Debug.WriteLine(
            $"[YouTubeChatPoller] メンバーシップギフト送信者を検知: {author} / {giftCount}人");

        _ = LocalServer.BroadcastSpecialChat(
            "membershipGiftPurchase",
            author,
            messageText);
    }

    private static bool IsGiftRecipientNotification(
        JsonElement giftRenderer,
        string visibleText)
    {
        // ここに到達する時点で送信者側の購入通知rendererに限定されている。
        // 念のため受取側を明示するフィールド・文言だけを除外する。
        if (ContainsTextRecursive(
            giftRenderer,
            "giftMembershipsReceived",
            "receivedMembership",
            "receivedGiftMembership",
            "membershipGiftReceived",
            "giftMembershipReceived",
            "recipientChannelId",
            "recipientChannelIdText"))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(visibleText))
            return false;

        string[] recipientPatterns =
        {
            "メンバーシップギフトを受け取りました",
            "メンバーシップギフトを受け取った",
            "ギフトメンバーシップを受け取りました",
            "ギフトメンバーシップを受け取った",
            "メンバーシップをプレゼントされました",
            "メンバーシップを贈られました",
            "メンバーシップギフトを獲得しました",
            "gift membership received",
            "gifted membership received",
            "you received a membership",
            "you received a gift membership",
            "received a membership",
            "received a gift membership",
            "has been gifted a membership",
            "got a gift membership"
        };

        foreach (var pattern in recipientPatterns)
        {
            if (visibleText.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool ContainsTextRecursive(
        JsonElement element,
        params string[] propertyNames)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                foreach (var target in propertyNames)
                {
                    if (property.Name.Equals(target, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                if (property.Value.ValueKind == JsonValueKind.Object ||
                    property.Value.ValueKind == JsonValueKind.Array)
                {
                    if (ContainsTextRecursive(property.Value, propertyNames))
                        return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                if (ContainsTextRecursive(child, propertyNames))
                    return true;
            }
        }

        return false;
    }

    private void ProcessGiftPurchase(JsonElement giftRenderer)
    {
        string author = "";

        // ギフトを送った人
        if (giftRenderer.TryGetProperty("header", out var header) &&
            header.TryGetProperty(
                "liveChatSponsorshipsHeaderRenderer",
                out var headerRenderer))
        {
            if (headerRenderer.TryGetProperty("authorName", out var authorName) &&
                authorName.TryGetProperty("simpleText", out var simpleText))
            {
                author = simpleText.GetString() ?? "";
            }
        }

        // 何人分のギフトを送ったか
        int giftCount = 0;

        if (giftRenderer.TryGetProperty(
            "message",
            out var message) &&
            message.TryGetProperty("runs", out var runs))
        {
            string text = "";

            foreach (var run in runs.EnumerateArray())
            {
                if (run.TryGetProperty("text", out var textElement))
                {
                    text += textElement.GetString() ?? "";
                }
            }

            // 例：
            // 「5 個のメンバーシップを贈りました」
            // のような表示から数字を取得
            var match = Regex.Match(text, @"(\d+)\s*(?:個|人)");

            if (match.Success)
            {
                int.TryParse(match.Groups[1].Value, out giftCount);
            }
        }

        if (giftCount <= 0)
        {
            // messageから取れなかった場合の予備処理
            string rendererText = giftRenderer.ToString();

            var match = Regex.Match(
                rendererText,
                @"""giftMembershipsCount""\s*:\s*(\d+)");

            if (match.Success)
            {
                int.TryParse(match.Groups[1].Value, out giftCount);
            }
        }

        if (string.IsNullOrEmpty(author) || giftCount <= 0)
            return;

        string messageText =
            $"{author} がメンバーシップギフトを {giftCount} 人に贈りました";

        System.Diagnostics.Debug.WriteLine(
            $"メンバーシップギフト: {author} / {giftCount}人");

        _ = LocalServer.BroadcastSpecialChat(
            "membershipGiftPurchase",
            author,
            messageText);
    }

    private async void ProcessRenderer(JsonElement renderer, HashSet<string> processedIds, Queue<string> idQueue, bool isMembership = false)
    {
        string author = "";
        if (renderer.TryGetProperty("authorName", out var authorNameNode) && authorNameNode.TryGetProperty("simpleText", out var simpleTextNode))
        {
            author = simpleTextNode.GetString() ?? "";
        }

        string messageText = "";

        // liveChatMembershipItemRenderer は通常加入通知だけでなく、
        // ギフトを受け取った側の通知にも使われる。
        // ギフト送信者は専用の購入通知 renderer だけで処理するため、
        // ここでは受取通知を除外し、通常加入だけを「member」として扱う。
        if (isMembership)
        {
            string membershipText = CollectTextRecursive(renderer);

            if (IsGiftRecipientNotification(renderer, membershipText))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[YouTubeChatPoller] メンバーシップギフト受取通知を無視: " +
                    $"author='{author}', text='{membershipText}'");
                return;
            }
        }

        // For membership join/gift, the text might be completely inside headerSubtext or just not have "message" property. Handle membership specific text first.
        if (isMembership && renderer.TryGetProperty("headerSubtext", out var headerSubtext) && headerSubtext.TryGetProperty("runs", out var headerRuns))
        {
            foreach (var run in headerRuns.EnumerateArray())
            {
                if (run.TryGetProperty("text", out var textEl))
                    messageText += textEl.GetString();
            }
        }
        else if (renderer.TryGetProperty("message", out var messageProp) && messageProp.TryGetProperty("runs", out var messageRuns))
        {
            foreach (var run in messageRuns.EnumerateArray())
            {
                if (run.TryGetProperty("text", out var textEl))
                {
                    messageText += textEl.GetString();
                }
                else if (run.TryGetProperty("emoji", out var emojiEl))
                {
                    string fallbackEmoji = "";

                    // If it's a standard unicode emoji encoded in `emojiId`, YouTube often passes the raw unicode character.
                    if (emojiEl.TryGetProperty("emojiId", out var emojiId))
                    {
                        var idStr = emojiId.GetString() ?? "";

                        // Some basic logic to detect if emojiId is raw unicode, or we could just add it directly.
                        if (!idStr.Contains(" ") && idStr.Length <= 4 && !idStr.StartsWith("UC"))
                        {
                            fallbackEmoji = idStr;
                        }
                    }

                    if (string.IsNullOrEmpty(fallbackEmoji) && emojiEl.TryGetProperty("shortcuts", out var shortcuts) && shortcuts.GetArrayLength() > 0)
                    {
                        string shortcut = shortcuts[0].GetString() ?? "";

                        try 
                        {
                            // If Emoji.Wpf had a mapping we'd use it, but typically shortcuts to emoji mapping isn't directly exposed.
                            // We can just rely on standard shortcut mapping or let YouTube supply unicode.
                            fallbackEmoji = shortcut; 
                        }
                        catch
                        {
                            fallbackEmoji = shortcut;
                        }
                    }

                    if (string.IsNullOrEmpty(fallbackEmoji))
                    {
                        fallbackEmoji = emojiEl.TryGetProperty("emojiId", out var eId) ? (eId.GetString() ?? "") : "";
                    }

                    messageText += fallbackEmoji;
                }
            }
        }
        
        // Also capture Paid message amounts.
        if (renderer.TryGetProperty("purchaseAmountText", out var purchaseAmountText) && purchaseAmountText.TryGetProperty("simpleText", out var purchaseSimpleText))
        {
            string amount = purchaseSimpleText.GetString() ?? "";
            // Fix yen mark displayed as backslash/slash
            amount = amount.Replace("¥", "￥").Replace("\\", "￥");
            messageText = $"[{amount}] {messageText}";
        }

        if (!string.IsNullOrEmpty(author))
        {
            // Translate common English membership text to Japanese
            if (messageText.Contains("said hi"))
            {
                messageText = messageText.Replace("said hi", "さんが「こんにちは」と言いました");
            }

            // If the message is completely empty (e.g. just a join without message), make sure we still process it.
            if (string.IsNullOrEmpty(messageText) && isMembership)
            {
                messageText = "メンバーシップに参加しました。";
            }

            if (!string.IsNullOrEmpty(messageText))
            {
                // Use message id when available, otherwise fall back to a content hash to prevent duplicates.
                string messageId = renderer.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                string normalizedText = messageText.Replace("\r", "").Replace("\n", "").Trim();
                string msgKey = string.IsNullOrWhiteSpace(messageId)
                    ? $"{author}:{normalizedText}"
                    : messageId;

                if (!processedIds.Contains(msgKey))
                {
                    processedIds.Add(msgKey);
                    idQueue.Enqueue(msgKey);

                    if (idQueue.Count > 200)
                    {
                        var oldKey = idQueue.Dequeue();
                        processedIds.Remove(oldKey);
                    }

                    if (normalizedText.StartsWith("[￥"))
                    {
                        _ = LocalServer.BroadcastSpecialChat("superchat", author, messageText);
                    }
                    else if (isMembership)
                    {
                        _ = LocalServer.BroadcastSpecialChat("member", author, messageText);
                    }
                    else
                    {
                        _ = LocalServer.BroadcastMessage(author, messageText);
                    }
                }
            }
        }
    }

    public void Stop()
    {
        _isRunning = false;
    }

    private string? ExtractVideoId(string url)
    {
        try
        {
            var uri = new Uri(url);
            if (uri.Host.Contains("youtu.be"))
            {
                return uri.AbsolutePath.TrimStart('/');
            }
            if (uri.Host.Contains("youtube.com"))
            {
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 2 && segments[0].Equals("live", StringComparison.OrdinalIgnoreCase))
                {
                    return segments[1];
                }

                var query = HttpUtility.ParseQueryString(uri.Query);
                return query["v"];
            }
        }
        catch { }
        return null;
    }
}
