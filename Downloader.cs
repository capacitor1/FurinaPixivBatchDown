using System;
using System.Net;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace FurinaPixivBatchDownloader
{
    internal class Downloader
    {
        public static int _429interval = 30000, _delay = 1000;//API 延迟间隔ms,这两个参数由配置决定

        private static readonly string ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36";
        private static DateTime _lastCallTime = DateTime.MinValue;
        private static readonly object _lockObj = new();
        public static int _downloading = 0;
        private static readonly HttpClient httpClient = new(), downloadClient = new();
        public static void InitClient(string? cookie)
        {
            downloadClient.DefaultRequestHeaders.Add("User-Agent", ua);
            downloadClient.DefaultRequestHeaders.Add("Origin", "https://www.pixiv.net");
            downloadClient.DefaultRequestHeaders.Add("Referer", "https://www.pixiv.net/");

            httpClient.DefaultRequestHeaders.Add("User-Agent", ua);
            if (!string.IsNullOrEmpty(cookie)) httpClient.DefaultRequestHeaders.Add("Cookie", cookie);
        }
        public static async Task PxDownload(string url, string save)//无限重试。
        {
            //判断文件是否已经下载完成
            if(File.Exists(save) && !File.Exists(save + ".down")) return;

            FileStream fs = new(save, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            var tmp = File.Create(save + ".down");//占位标记
            _downloading++;
            while (true)
            {
                try
                {
                    fs.Position = 0;
                    HttpResponseMessage response = await downloadClient.SendAsync(new HttpRequestMessage
                    {
                        Method = HttpMethod.Get,
                        RequestUri = new Uri(url),
                    });
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        if ((int)response.StatusCode == 404)
                        {
                            Console.WriteLine($"[DOWN E] Ignored '{Path.GetFileName(fs.Name)}' because HTTP 404");
                            LogErr($"[Downloader] HTTP 404 : {url} , save path : {save}");
                            fs.Dispose();
                            return;
                        }
                        else
                        {
                            throw new Exception($"{response.StatusCode}({(int)response.StatusCode})");
                        }
                    }
                    using (Stream contentStream = await response.Content.ReadAsStreamAsync()) await contentStream.CopyToAsync(fs);
                    Console.WriteLine($"[DOWN I] '{Path.GetFileName(fs.Name)}'({fs.Length}B) finished");
                    fs.Dispose();
                    tmp.Dispose();
                    _downloading--;
                    File.Delete(save + ".down");//下载完成，删除标记
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DOWN W] '{Path.GetFileName(fs.Name)}' error : {ex.Message}");
                }
            }
        }
        public static async Task<JsonNode> PxGet(string api,string referer)
        { 
            //
            int waitMs = 0;
            lock (_lockObj)
            {
                var now = DateTime.Now;
                var timeSinceLastCall = now - _lastCallTime;
                
                if (timeSinceLastCall.TotalMilliseconds < _delay) waitMs = (int)(_delay - timeSinceLastCall.TotalMilliseconds);
                _lastCallTime = now.AddMilliseconds(waitMs);
            }

            //
            if (waitMs > 0) await Task.Delay(waitMs);

            //
            string str = api[26..];
            JsonNode? tmp = null;
            int _429 = _429interval;
            httpClient.DefaultRequestHeaders.Referrer = new(referer);
            while (tmp is null)
            {
                try
                {
                    HttpResponseMessage responseMessage = await httpClient.SendAsync(new HttpRequestMessage
                    {
                        Method = HttpMethod.Get,
                        RequestUri = new Uri(api),
                    });
                    if((int)responseMessage.StatusCode == 429)//pixiv 429
                    {
                        Console.WriteLine($"[API  W] {str} : HTTP 429.Waiting for {_429} ms ...");
                        await Task.Delay(_429);
                        _429 *= 2;//x2
                    }
                    else if ((int)responseMessage.StatusCode >= 400 && (int)responseMessage.StatusCode < 500)
                    {
                        //client error
                        Console.WriteLine($"[API  E] {str} : HTTP {(int)responseMessage.StatusCode}");
                        LogErr($"[API] HTTP error {(int)responseMessage.StatusCode} at {api}");
                    }
                    else
                    {
                        tmp = JsonNode.Parse(await responseMessage.Content.ReadAsStringAsync())!;
                        if ((bool)tmp["error"]!)
                        {
                            Console.WriteLine($"[API  W] {str} : json error '{(string)tmp["message"]!}'");
                            await Task.Delay(500);//prevent client ban
                            tmp = null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[API  W] {str} : {ex.Message}");
                }
                await Task.Delay(1000);//失败间隔
            }
            Console.WriteLine($"[API  I] {str} : OK");
            return tmp;
        }

        public static void LogErr(string msg) => File.AppendAllLines("FPBD_Log.txt", [msg]);
    }
}
