using System;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace FurinaPixivBatchDownloader
{
    internal static class Downloader
    {
        public static int _429interval = 30000, _delay = 1000; //API 延迟间隔ms,这两个参数由配置决定

        private const string ua =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36";
        private static DateTime _lastCallTime = DateTime.UtcNow;
        private static readonly HttpClient httpClient = new(), downloadClient = new();

        //stats
        public static long _totalDl = 0,
            _totalDlFile = 0,
            _apiok = 0,
            _totalusers = 0,
            _dlusers = 0,
            _totalitems = 0,
            _dlitems = 0;

        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(50, 50);
        public static int GetCurrent() => 50 - _semaphore.CurrentCount;

        public static void PrintStatiticsToConsole()
        {
            /*
            if(Console.CursorLeft == 0)//没有输入内容，可覆盖
            {
                int currentTop = Console.CursorTop;
                Console.SetCursorPosition(0, currentTop - 1);

                string emptyLine = new string(' ', Console.WindowWidth);
                Console.Write(emptyLine);

                Console.SetCursorPosition(0, currentTop - 1);
            }
            */
            Console.WriteLine(
                $"[MAIN I] Download state : {_dlusers} / {_totalusers} ({_dlitems} / {_totalitems}) [API = {_apiok}, Total = {_totalDlFile} ({_totalDl:N0} bytes), Active = {GetCurrent()}]");
        }

        public static void Reset()
        {
            _totalDl = _totalDlFile = _apiok = _totalusers = _dlusers = _totalitems = _dlitems = 0;
        }

        public static void InitClient(string? cookie)
        {
            downloadClient.DefaultRequestHeaders.Add("User-Agent", ua);
            downloadClient.DefaultRequestHeaders.Add("Origin", "https://www.pixiv.net");
            downloadClient.DefaultRequestHeaders.Add("Referer", "https://www.pixiv.net/");
            httpClient.DefaultRequestHeaders.Add("User-Agent", ua);
            if (!string.IsNullOrEmpty(cookie)) httpClient.DefaultRequestHeaders.Add("Cookie", cookie);
        }

        public static async Task PxDownload(string url, string save)
        {
            if (File.Exists(save) && !File.Exists(save + ".down"))
                return;

            await _semaphore.WaitAsync();

            try
            {
                while (true)
                {
                    try
                    {
                        string downFile = save + ".down";

                        long offset = 0;
                        string? localETag = null;
                        long localLength = 0;

                        if (File.Exists(save) && File.Exists(downFile))
                        {
                            string[] lines = await File.ReadAllLinesAsync(downFile);

                            if (lines.Length >= 2)
                            {
                                localETag = lines[0];
                                long.TryParse(lines[1], out localLength);

                                offset = new FileInfo(save).Length;
                            }
                        }

                        using HttpRequestMessage req = new(HttpMethod.Get, url);

                        if (offset > 0)
                            req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, null);

                        using HttpResponseMessage response =
                            await downloadClient.SendAsync(
                                req,
                                HttpCompletionOption.ResponseHeadersRead);

                        //-------------------------------------
                        // 首次下载
                        //-------------------------------------

                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string? etag = response.Headers.ETag?.Tag;
                            long totalLength = response.Content.Headers.ContentLength ?? 0;

                            await File.WriteAllLinesAsync(downFile,
                            [
                                etag ?? "",
                                totalLength.ToString()
                            ]);

                            await using FileStream fs = new(
                                save,
                                FileMode.Create,
                                FileAccess.Write,
                                FileShare.Read,
                                65536,
                                FileOptions.SequentialScan);

                            await using Stream stream = await response.Content.ReadAsStreamAsync();

                            await stream.CopyToAsync(fs, 262144);

                            Interlocked.Add(ref _totalDl, fs.Length);
                            Interlocked.Increment(ref _totalDlFile);

                            File.Delete(downFile);

                            break;
                        }

                        //-------------------------------------
                        // 断点续传
                        //-------------------------------------

                        if (response.StatusCode == HttpStatusCode.PartialContent)
                        {
                            string? serverETag = response.Headers.ETag?.Tag;

                            if (serverETag != localETag)
                            {
                                File.Delete(save);
                                File.Delete(downFile);
                                continue;
                            }

                            long totalLength =
                                response.Content.Headers.ContentRange?.Length
                                ?? 0;

                            if (totalLength != localLength)
                            {
                                File.Delete(save);
                                File.Delete(downFile);
                                continue;
                            }

                            await using FileStream fs = new(
                                save,
                                FileMode.Append,
                                FileAccess.Write,
                                FileShare.Read,
                                65536,
                                FileOptions.SequentialScan);

                            await using Stream stream = await response.Content.ReadAsStreamAsync();

                            await stream.CopyToAsync(fs, 262144);

                            Interlocked.Add(ref _totalDl, fs.Length);
                            Interlocked.Increment(ref _totalDlFile);

                            File.Delete(downFile);

                            break;
                        }

                        //-------------------------------------
                        // Range 不支持
                        //-------------------------------------
                        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                        {
                            File.Delete(save);
                            File.Delete(downFile);
                            continue;
                        }
                        //-------------------------------------
                        // 404
                        //-------------------------------------

                        if (response.StatusCode == HttpStatusCode.NotFound)
                        {
                            Console.WriteLine($"[DOWN E] Ignored '{Path.GetFileName(save)}' because HTTP 404");
                            LogErr($"[Downloader] HTTP404 : {url}");
                            File.Delete(downFile);
                            break;
                        }

                        throw new Exception($"{response.StatusCode} ({(int)response.StatusCode})");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[DOWN W] '{Path.GetFileName(save)}' error : {ex.Message}");
                        await Task.Delay(3000);
                    }
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private static readonly SemaphoreSlim _apiRateSemaphore = new(1, 1);

        public static async Task<JsonNode?> PxGet(string api, string referer)
        {
            string str = api[26..];

            int retry429 = _429interval;

            while (true)
            {
                try
                {
                    //--------------------------------------------------
                    // 全局 API 限速（线程安全）
                    //--------------------------------------------------

                    await _apiRateSemaphore.WaitAsync();
                    try
                    {
                        TimeSpan elapsed = DateTime.UtcNow - _lastCallTime;

                        if (elapsed.TotalMilliseconds < _delay)
                        {
                            await Task.Delay(_delay - (int)elapsed.TotalMilliseconds);
                        }

                        // 在发送请求前更新时间
                        _lastCallTime = DateTime.UtcNow;
                    }
                    finally
                    {
                        _apiRateSemaphore.Release();
                    }

                    //--------------------------------------------------
                    // 创建请求
                    //--------------------------------------------------

                    using HttpRequestMessage request = new(HttpMethod.Get, api);
                    request.Headers.Referrer = new Uri(referer);

                    using HttpResponseMessage response =
                        await httpClient.SendAsync(request);

                    //--------------------------------------------------
                    // HTTP 状态处理
                    //--------------------------------------------------

                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        Console.WriteLine($"[API  W] {str} : HTTP 429. Waiting {retry429} ms...");
                        await Task.Delay(retry429);
                        retry429 *= 2;
                        continue;
                    }

                    if ((int)response.StatusCode >= 500)
                    {
                        Console.WriteLine($"[API  W] {str} : HTTP {(int)response.StatusCode}");
                        await Task.Delay(1000);
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[API  E] {str} : HTTP {(int)response.StatusCode}");
                        LogErr($"[API] HTTP {(int)response.StatusCode} : {api}");
                        return null;
                    }

                    //--------------------------------------------------
                    // JSON
                    //--------------------------------------------------

                    JsonNode? json =
                        JsonNode.Parse(await response.Content.ReadAsStringAsync());

                    if (json == null)
                    {
                        Console.WriteLine($"[API  W] {str} : Empty JSON");
                        await Task.Delay(1000);
                        continue;
                    }

                    if (json["error"]?.GetValue<bool>() == true)
                    {
                        string msg = json["message"]?.GetValue<string>() ?? "Unknown";
                        Console.WriteLine($"[API  W] {str} : JSON error '{msg}'");
                        // 防止客户端被限制
                        await Task.Delay(500);

                        return null;
                    }

                    Interlocked.Increment(ref _apiok);

                    return json;
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine($"[API  W] {str} : {ex.Message}");
                }
                catch (TaskCanceledException ex)
                {
                    Console.WriteLine($"[API  W] {str} : {ex.Message}");
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"[API  W] {str} : {ex.Message}");
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"[API  W] {str} : JSON parse failed : {ex.Message}");

                    return null;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[API  W] {str} : {ex.Message}");
                }
                await Task.Delay(1000);
            }
        }

        private static void LogErr(string msg) => File.AppendAllLines("FPBD_Log.txt", [msg]);
    }
}