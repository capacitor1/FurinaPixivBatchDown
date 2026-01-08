using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FurinaPixivBatchDownloader
{
    internal class Program
    {
        //settings
        public static JsonSerializerOptions options = new() { WriteIndented = true };
        static async Task Main()
        {
            //解析配置文件
            var _config = ConfigLoader.Load();
            Downloader._429interval = _config.Init429Delay ?? 30000;//以防配置文件是null
            Downloader._delay = _config.ApiRequestDelay ?? 1000;

            Downloader.InitClient(_config.Cookie);

            //输入pixiv URLs
            string input = _config.AutoLoadUsersList ?? string.Empty;
            while (string.IsNullOrEmpty(input))
            {
                Console.WriteLine("[MAIN I] Input pixiv users URL(or path to users list):");
                var t = Console.ReadLine();
                input = t ?? input;
            }
            List<string?> _pxusersid = [];//users id
            if (File.Exists(input))
            {
                //read urls
                string[] l = File.ReadAllLines(input);
                foreach (string line in l) _pxusersid.Add(GetUserId(line));
            }
            else
            {
                //single url
                _pxusersid.Add(GetUserId(input));
            }
            List<string> pxuserids = _pxusersid.Where(item => item != null).ToList()!;//去除上述可能的null项
            Console.WriteLine($"[MAIN I] Processed {_pxusersid.Count} lines and gathered {pxuserids.Count} users.");
            await Task.Delay(500);

            //开始逐一下载用户
            foreach (string pxuserid in pxuserids)
            {
                //获取用户
                JsonNode userjson = await Downloader.PxGet(
                    $"https://www.pixiv.net/ajax/user/{pxuserid}/profile/all",
                    $"https://www.pixiv.net/en/users/{pxuserid}"
                    );
                JsonNode userprofil = await Downloader.PxGet(
                    $"https://www.pixiv.net/ajax/user/{pxuserid}?full=1",
                    $"https://www.pixiv.net/en/users/{pxuserid}"
                    );

                //创建文件夹
                string name = FileNameHelper.ToValidFileName($"{(string)userprofil["body"]!["name"]!} [{(string)userprofil["body"]!["userId"]!}]");
                string _basefolder = Path.Combine(_config.SaveBasePath is null ? Environment.CurrentDirectory : _config.SaveBasePath, name);
                Directory.CreateDirectory(_basefolder);

                //写入profile
                string jpath = Path.Combine(_basefolder, "profile.json");
                File.WriteAllText(jpath, JsonSerializer.Serialize(userprofil, options));
                await Downloader.PxDownload((string)userprofil["body"]!["imageBig"]!, Path.Combine(_basefolder, "avatar.png"));
                if(userprofil["body"]!["background"] is not null)
                    await Downloader.PxDownload((string)userprofil["body"]!["background"]!["url"]!, Path.Combine(_basefolder, "bg.png"));
                //获取其中所有作品ID
                List<string> allworks = [],novels = [];

                //illust
                if (userjson["body"]!["illusts"] is JsonObject o) allworks.AddRange(o.Select(p => p.Key));

                //manga
                if (userjson["body"]!["manga"] is JsonObject o1) allworks.AddRange(o1.Select(p => p.Key));

                //novel
                if (userjson["body"]!["novels"] is JsonObject o2) novels.AddRange(o2.Select(p => p.Key));

                Console.WriteLine($"[MAIN I] Get {allworks.Count} works and {novels.Count} novels from {pxuserid}.");
                //开始下载
                foreach (var il in allworks)
                {
                    //检查是否有大量任务残留下载，防止爆炸
                    while (Downloader._downloading > 50)
                    {
                        Console.WriteLine($"[MAIN W] Remaining {Downloader._downloading} files are downloading,waiting...");
                        await Task.Delay(5000);
                    }

                    string _savefolder = Path.Combine(_basefolder, "Illusts"), jp = Path.Combine(_savefolder, $"{il}_idx.json"); 
                    Directory.CreateDirectory(_savefolder);
                    JsonNode illust;
                    //获取作品
                    if (File.Exists(jp))
                    {
                        try
                        {
                            illust = JsonNode.Parse(File.ReadAllText(jp))!;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[MAIN W] Cached local file error: {ex.Message}.Retry from pixiv.");
                            illust = await Downloader.PxGet(
                                $"https://www.pixiv.net/ajax/illust/{il}?time={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                                $"https://www.pixiv.net/en/users/{pxuserid}"
                            );
                        }
                    }
                    else
                    {
                        illust = await Downloader.PxGet(
                            $"https://www.pixiv.net/ajax/illust/{il}?time={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                            $"https://www.pixiv.net/en/users/{pxuserid}"
                        );
                    }
                    //保存作品
                    if ((int)illust["body"]!["aiType"]! > 0 && !_config.NeedAI) continue;// 跳过AI
                    if (!File.Exists(jp)) File.WriteAllText(jp, JsonSerializer.Serialize(illust, options));

                    //获取URL中的信息
                    string baseurl = (string)illust["body"]!["urls"]!["original"]!;
                    string ext = baseurl.Split('.').Last();
                    //动画
                    if (baseurl.Contains("_ugoira0"))
                    {
                        jp = Path.Combine(_savefolder, $"{il}_ugoira.json");
                        JsonNode u;
                        if (File.Exists(jp))
                        {
                            try
                            {
                                u = JsonNode.Parse(File.ReadAllText(jp))!;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[MAIN W] Cached local file error: {ex.Message}.Retry from pixiv.");
                                u = await Downloader.PxGet($"https://www.pixiv.net/ajax/illust/{il}/ugoira_meta",$"https://www.pixiv.net/en/artworks/{il}");
                            }
                        }
                        else
                        {
                            u = await Downloader.PxGet($"https://www.pixiv.net/ajax/illust/{il}/ugoira_meta",$"https://www.pixiv.net/en/artworks/{il}");
                        }

                        //保存
                        if (!File.Exists(jp)) File.WriteAllText(jp, JsonSerializer.Serialize(u, options));
                        //下载
                        string url = (string)u["body"]!["originalSrc"]!,imgname = url.Split('/').Last();
                        _ = Downloader.PxDownload(url, Path.Combine(_savefolder, imgname));

                        //直接结束
                        continue;
                    }
                    //图片
                    baseurl = baseurl[..(baseurl.LastIndexOf('/') + 1)];
                    int page = (int)illust["body"]!["pageCount"]!;//ex.5 (p0-p4)
                    //下载
                    while (page > 0)
                    {
                        string imgname = $"{il}_p{page - 1}.{ext}";
                        _ = Downloader.PxDownload($"{baseurl}{imgname}", Path.Combine(_savefolder, imgname));
                        page--;
                    }
                }

                //novels
                foreach (string novID in novels)
                {
                    string _savefolder = Path.Combine(_basefolder, "Novels"), jp = Path.Combine(_savefolder, $"{novID}_idx.json");
                    Directory.CreateDirectory(_savefolder);
                    JsonNode n;
                    //获取小说
                    if (File.Exists(jp))
                    {
                        try
                        {
                            n = JsonNode.Parse(File.ReadAllText(jp))!;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[MAIN W] Cached local file error: {ex.Message}.Retry from pixiv.");
                            n = await Downloader.PxGet($"https://www.pixiv.net/ajax/novel/{novID}?time={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                                $"https://www.pixiv.net/en/users/{pxuserid}");
                        }
                    }
                    else
                    {
                        n = await Downloader.PxGet($"https://www.pixiv.net/ajax/novel/{novID}?time={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                                $"https://www.pixiv.net/en/users/{pxuserid}");
                    }

                    //保存
                    if (!File.Exists(jp))
                    {
                        File.WriteAllText(jp, JsonSerializer.Serialize(n, options));
                        //提取TXT
                        string path = Path.Combine(_savefolder, $"{novID}.txt");
                        File.WriteAllText(path, (string)n["body"]!["content"]!);
                    }

                    //下载封面
                    _ = Downloader.PxDownload((string)n["body"]!["coverUrl"]!, Path.Combine(_savefolder, $"{novID}_cover.jpg"));

                    Console.WriteLine($"[MAIN I] Novel {novID} finished.");
                }
            }

            //check 
            while(Downloader._downloading > 0)
            {
                Console.WriteLine($"[MAIN I] API requests finished,but remaining {Downloader._downloading} files are downloading");
                await Task.Delay(5000);
            }
            //end
            Console.WriteLine("[MAIN I] Done!\r\n[MAIN I] Press any key to exit.");
            Console.ReadKey();
        } 
        
        // 匹配 Pixiv 用户主页 URL 的正则表达式
        private static readonly Regex _pixivUserIdRegex = new(@"^https://www\.pixiv\.net/(?:\w{2}/)?users/(\d+)",RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="raw"></param>
        /// <returns>提取到的用户 ID，如果 URL 格式不符合预期则返回 null。</returns>
        public static string? GetUserId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            Match match = _pixivUserIdRegex.Match(raw);
            if (match.Success) return match.Groups[1].Value;
            return null;
        }
    }
}