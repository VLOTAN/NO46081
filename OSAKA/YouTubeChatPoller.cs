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

                                if (item.TryGetProperty(
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
                                else if (item.TryGetProperty(
                                             "liveChatSponsorshipsGiftPurchaseAnnouncementRenderer",
                                             out var giftRenderer))
                                {
                                    ProcessGiftPurchaseRenderer(
                                        giftRenderer,
                                        _processedMessageIds,
                                        _messageIdQueue);
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

    private void ProcessGiftPurchaseRenderer(
    JsonElement giftRenderer,
    HashSet<string> processedIds,
    Queue<string> idQueue)
    {
        string author = "";
        int giftCount = 0;
        string messageId = "";

        // ギフトを送った人の名前
        if (giftRenderer.TryGetProperty("header", out var header) &&
            header.TryGetProperty(
                "liveChatSponsorshipsHeaderRenderer",
                out var headerRenderer))
        {
            if (headerRenderer.TryGetProperty(
                "authorName",
                out var authorName) &&
                authorName.TryGetProperty(
                    "simpleText",
                    out var authorText))
            {
                author = authorText.GetString() ?? "";
            }
        }

        // ギフト人数
        if (giftRenderer.TryGetProperty(
            "giftMembershipsCount",
            out var giftCountElement))
        {
            if (giftCountElement.ValueKind == JsonValueKind.Number)
            {
                giftCount = giftCountElement.GetInt32();
            }
        }

        // メッセージID
        if (giftRenderer.TryGetProperty(
            "id",
            out var idElement))
        {
            messageId = idElement.GetString() ?? "";
        }

        // 必要な情報が取れなかった場合は無視
        if (string.IsNullOrWhiteSpace(author) || giftCount <= 0)
        {
            System.Diagnostics.Debug.WriteLine(
                $"ギフト情報取得失敗: author={author}, count={giftCount}");

            return;
        }

        // 重複防止
        string msgKey = string.IsNullOrWhiteSpace(messageId)
            ? $"{author}:membershipGift:{giftCount}"
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

        // アプリ側へ通知
        string messageText =
            $"{author} がメンバーシップギフトを {giftCount} 人に贈りました";

        System.Diagnostics.Debug.WriteLine(
            $"メンバーシップギフト: {author} / {giftCount}人");

        _ = LocalServer.BroadcastSpecialChat(
            "membershipGiftPurchase",
            author,
            messageText);
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
