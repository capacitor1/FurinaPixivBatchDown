using System;
using System.Reflection.Emit;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FurinaPixivBatchDownloader
{
    internal class Program
    {
        private static bool isStop = false, novels = false;
        //settings
        private static JsonSerializerOptions options = new() { WriteIndented = true };
        static async Task Main()
        {
            //解析配置文件
            var _config = ConfigLoader.Load();
            Downloader._429interval = _config.Init429Delay ?? 30000;//以防配置文件是null
            Downloader._delay = _config.ApiRequestDelay ?? 1000;
            List<uint> users = [..FileNameHelper.ReadUInt32FromFile(_config.AutoLoadUsersList!)];

            Downloader.InitClient(_config.Cookie);
            bool isRunning = false;
            //循环执行
            var uiTask = Task.Run(async () =>
            {
                Console.WriteLine("[TIPS I] Input 'Help' to show help.");
                while ( true )
                {
                    //input
                    var i = Console.ReadLine();
                    if(string.IsNullOrWhiteSpace(i)) continue;
                    if (i.Equals("updatenow", StringComparison.OrdinalIgnoreCase))
                    {
                        _ = Task.Run(async () =>
                        {
                            if (!isRunning)
                            {
                                isRunning = true;
                                await TaskRun(_config, [.. users], Path.Combine(_config.SaveBasePath!, "DownloadedIllusts.bytes"), Path.Combine(_config.SaveBasePath!, "DownloadedNovels.bytes"));
                                isRunning = false;
                            }
                        });
                    }
                    else if (i.Equals("help", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"[TIPS I] Input Pixiv user id or link to add users.");
                        Console.WriteLine($"[TIPS I] Input 'UpdateNow' to start task immediately.");
                        Console.WriteLine($"[TIPS I] Input 'Exit' to exit app after save all data.");
                        Console.WriteLine($"[TIPS I] Input 'Stop' to stop task and save all data.");
                        Console.WriteLine($"[TIPS I] Input 'ListAll' to list all users.");
                        Console.WriteLine($"[TIPS I] Input 'Novels' to toggle novels download.");
                    }
                    else if (i.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    {
                        while (isRunning)
                        {
                            Console.WriteLine($"[WAIT I] Waiting task...");
                            await Task.Delay(30000);
                        }
                        Environment.Exit(0);
                    }
                    else if(i.Equals("stop", StringComparison.OrdinalIgnoreCase))
                    {
                        if (isRunning)
                        {
                            isStop = true;
                            Console.WriteLine($"[WAIT I] Stopping task...");
                        }
                        else
                        {
                            Console.WriteLine($"[WAIT I] Task not running.");
                        }
                    }
                    else if (i.Equals("listall", StringComparison.OrdinalIgnoreCase))
                    {
                        StringBuilder sb = new StringBuilder();
                        foreach (var user in users)
                        {
                            sb.Append(user);
                            sb.Append(',');
                        }
                        Console.WriteLine($"[MAIN I] All user IDs : {sb}");
                    }
                    else if (i.Equals("novels", StringComparison.OrdinalIgnoreCase))
                    {
                        novels = !novels;
                        Console.WriteLine($"[MAIN I] Novels download state : {novels}");
                    }
                    else//add users
                    {
                        if(uint.TryParse(i,out uint toadd))
                        {
                            if (!users.Contains(toadd))
                            {
                                users.Insert(0,toadd);
                                FileNameHelper.WriteUInt32ToFile([.. users], _config.AutoLoadUsersList!);
                                Console.WriteLine($"[MAIN I] Added user '{toadd}'");
                            }
                            else
                            {
                                Console.WriteLine($"[MAIN I] User exists '{toadd}'");
                            }
                        }
                        else
                        {
                            string? id = GetUserId(i);
                            if(id == null)
                            {
                                Console.WriteLine($"[MAIN I] Invalid Input '{i}'");
                            }
                            else
                            {
                                uint a = uint.Parse(id);
                                if (!users.Contains(a))
                                {
                                    users.Insert(0, a);
                                    FileNameHelper.WriteUInt32ToFile([.. users], _config.AutoLoadUsersList!);
                                    Console.WriteLine($"[MAIN I] Added user '{a}'");
                                }
                                else
                                {
                                    Console.WriteLine($"[MAIN I] User exists '{a}'");
                                }
                            }
                        }
                    }
                }
            });
            //run
            var mainTask = Task.Run(async () =>
            {
                while (true)
                {
                    //wait
                    await Task.Delay((int)_config.UpDateInterval!);
                    if (!isRunning)
                    {
                        isRunning = true;
                        await TaskRun(_config, [.. users], Path.Combine(_config.SaveBasePath!, "DownloadedIllusts.bytes"), Path.Combine(_config.SaveBasePath!, "DownloadedNovels.bytes"));
                        isRunning = false;
                    }
                }
            });

            //
            while (true)
            {
                await Task.Delay(int.MaxValue);
            }
        }

        // 匹配 Pixiv 用户主页 URL 的正则表达式
        private static readonly Regex _pixivUserIdRegex = new(@"^https://www\.pixiv\.net/(?:\w{2}/)?users/(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="raw"></param>
        /// <returns>提取到的用户 ID，如果 URL 格式不符合预期则返回 null。</returns>
        private static string? GetUserId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            Match match = _pixivUserIdRegex.Match(raw);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static async Task TaskRun(AppConfig _config, uint[] usersID, string downloadedillustspth, string downloadednovelspth)
        {
            Downloader.Reset();
            SemaphoreSlim semaphoreSlim = new SemaphoreSlim(50, 50);
            HashSet<uint> _downloadedIllusts = File.Exists(downloadedillustspth) ? [..FileNameHelper.ReadUInt32FromFile(downloadedillustspth)] : [];
            HashSet<uint> _downloadedNovels = File.Exists(downloadednovelspth) ? [..FileNameHelper.ReadUInt32FromFile(downloadednovelspth)] : [];
            Console.WriteLine($"[MAIN I] Checked downloaded {_downloadedIllusts.Count}I + {_downloadedNovels.Count}N = {_downloadedIllusts.Count + _downloadedNovels.Count} items and {usersID.Length} users.");

            int _dl = 0;
            bool ui = true;
            await Task.Delay(500);
            //UI进度显示
            var uirun = Task.Run(async () =>
            {
                while (ui)
                {
                    Downloader.PrintStatiticsToConsole();
                    await Task.Delay(5000);
                }
            });
            Downloader._totalusers = usersID.Length;
            //开始逐一下载用户
            foreach (uint pxuserid in usersID)
            {
                if (isStop) goto EndTask;
                //获取用户
                JsonNode? userjson = await Downloader.PxGet(
                    $"https://www.pixiv.net/ajax/user/{pxuserid}/profile/all",
                    $"https://www.pixiv.net/en/users/{pxuserid}"
                    );
                JsonNode? userprofil = await Downloader.PxGet(
                    $"https://www.pixiv.net/ajax/user/{pxuserid}?full=1",
                    $"https://www.pixiv.net/en/users/{pxuserid}"
                    );
                //检查json有效性
                if (userjson == null || userprofil == null)
                {
                    Console.WriteLine($"[API  E] Invalid profile at {pxuserid} , skippping...");
                    continue;
                }
                //创建文件夹
                string name = FileNameHelper.ToValidFileName($"{(string)userprofil["body"]!["name"]!} [{(string)userprofil["body"]!["userId"]!}]");
                string _basefolder = Path.Combine(_config.SaveBasePath ?? Environment.CurrentDirectory, name);
                Directory.CreateDirectory(_basefolder);

                //写入profile
                string jpath = Path.Combine(_basefolder, "profile.json");
                byte[] pcontent = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(userprofil, options));
                //是否存在？是否一致？
                if (File.Exists(jpath))
                {
                    byte[] s256o = Hash.GetSha256(jpath);
                    byte[] s256n = Hash.GetSha256(pcontent);
                    if (!s256o.SequenceEqual(s256n))
                    {
                        //不一致，写入新的。
                        FileNameHelper.AddToBackUpZipAndDelete(jpath);
                        await File.WriteAllBytesAsync(jpath, pcontent);
                    }
                }
                else
                {
                    await File.WriteAllBytesAsync(jpath, pcontent);
                }
                //userjson直接覆盖写入
                await File.WriteAllTextAsync(Path.Combine(_basefolder, "list.json"), JsonSerializer.Serialize(userjson, options));
                await Downloader.PxDownload((string)userprofil["body"]!["imageBig"]!, Path.Combine(_basefolder, "avatar.png"));
                if (userprofil["body"]!["background"] is not null)
                    await Downloader.PxDownload((string)userprofil["body"]!["background"]!["url"]!, Path.Combine(_basefolder, "bg.png"));
                //获取其中所有作品ID
                List<string> allworks = [], novels = [];

                //illust
                if (userjson["body"]!["illusts"] is JsonObject o) allworks.AddRange(o.Select(p => p.Key));

                //manga
                if (userjson["body"]!["manga"] is JsonObject o1) allworks.AddRange(o1.Select(p => p.Key));

                //novel
                if (userjson["body"]!["novels"] is JsonObject o2) novels.AddRange(o2.Select(p => p.Key));

                //Console.WriteLine($"[MAIN I] Get {allworks.Count} works and {novels.Count} novels from {pxuserid}.");
                Downloader._totalitems += allworks.Count;
                Downloader._totalitems += novels.Count;
                //开始下载
                foreach (var il in allworks)
                {
                    if (isStop) goto EndTask;

                    if (_downloadedIllusts.Contains(uint.Parse(il))) { Downloader._dlitems++; continue; }//已存在，跳过

                    string _savefolder = Path.Combine(_basefolder, "Illusts"), jp = Path.Combine(_savefolder, $"{il}_idx.json");
                    Directory.CreateDirectory(_savefolder);
                    JsonNode? illust;
                    //获取作品
                    if (File.Exists(jp))
                    {
                        try
                        {
                            illust = JsonNode.Parse(await File.ReadAllTextAsync(jp))!;
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
                    if (illust == null) continue;
                    //保存作品
                    if ((int)illust["body"]!["aiType"]! > 0 && !_config.NeedAI) continue;// 跳过AI
                    if (!File.Exists(jp)) await File.WriteAllTextAsync(jp, JsonSerializer.Serialize(illust, options));

                    //获取URL中的信息
                    string baseurl = (string)illust["body"]!["urls"]!["original"]!;
                    string ext = baseurl.Split('.').Last();
                    //动画
                    if (baseurl.Contains("_ugoira0"))
                    {
                        jp = Path.Combine(_savefolder, $"{il}_ugoira.json");
                        JsonNode? u;
                        if (File.Exists(jp))
                        {
                            try
                            {
                                u = JsonNode.Parse(await File.ReadAllTextAsync(jp))!;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[MAIN W] Cached local file error: {ex.Message}.Retry from pixiv.");
                                u = await Downloader.PxGet($"https://www.pixiv.net/ajax/illust/{il}/ugoira_meta", $"https://www.pixiv.net/en/artworks/{il}");
                            }
                        }
                        else
                        {
                            u = await Downloader.PxGet($"https://www.pixiv.net/ajax/illust/{il}/ugoira_meta", $"https://www.pixiv.net/en/artworks/{il}");
                        }
                        if (u == null) continue;
                        //保存
                        if (!File.Exists(jp)) await File.WriteAllTextAsync(jp, JsonSerializer.Serialize(u, options));
                        //下载
                        string url = (string)u["body"]!["originalSrc"]!, imgname = url.Split('/').Last();
                        _ = Downloader.PxDownload(url, Path.Combine(_savefolder, imgname));

                        //直接结束
                        _downloadedIllusts.Add(uint.Parse(il));
                        Downloader._dlitems++;
                        _dl++;
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
                    _downloadedIllusts.Add(uint.Parse(il));
                    Downloader._dlitems++;
                    _dl++;
                }

                //novels
                foreach (string novID in novels)
                {
                    if (isStop) goto EndTask;
                    if (!Program.novels){ Downloader._dlitems++; continue; }
                    string _savefolder = Path.Combine(_basefolder, "Novels"), jp = Path.Combine(_savefolder, $"{novID}_idx.json");
                    Directory.CreateDirectory(_savefolder);
                    JsonNode? n = await Downloader.PxGet($"https://www.pixiv.net/ajax/novel/{novID}?time={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                                $"https://www.pixiv.net/en/users/{pxuserid}");
                    if (n == null) continue;
                    //保存
                    pcontent = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(n, options));
                    string path = Path.Combine(_savefolder, $"{novID}.txt");
                    Downloader._totalDl += pcontent.LongLength;
                    Downloader._totalDlFile++;
                    //是否存在？是否一致？
                    if (File.Exists(jp))
                    {
                        byte[] s256o = Hash.GetSha256(jp);
                        byte[] s256n = Hash.GetSha256(pcontent);
                        if (!s256o.SequenceEqual(s256n))
                        {
                            //不一致，写入新的。
                            FileNameHelper.AddToBackUpZipAndDelete(jp);
                            await File.WriteAllBytesAsync(jp, pcontent);
                            //提取TXT
                            await File.WriteAllTextAsync(path, (string)n["body"]!["content"]!);
                        }
                    }
                    else
                    {
                        await File.WriteAllBytesAsync(jp, pcontent);
                        //提取TXT
                        await File.WriteAllTextAsync(path, (string)n["body"]!["content"]!);
                    }

                    //下载封面
                    _ = Downloader.PxDownload((string)n["body"]!["coverUrl"]!, Path.Combine(_savefolder, $"{novID}_cover.jpg"));

                    //Console.WriteLine($"[MAIN I] Novel {novID} finished.");

                    _downloadedNovels.Add(uint.Parse(novID));
                    Downloader._dlitems++;
                    _dl++;
                }
                Downloader._dlusers++;
            }
            EndTask:
            //check 
            while (Downloader.GetCurrent() > 0)
            {
                Console.WriteLine($"[MAIN I] Downloading {Downloader.GetCurrent()} files before finish task...");
                await Task.Delay(10000);
            }
            //write dllst
            FileNameHelper.WriteUInt32ToFile([.._downloadedIllusts], downloadedillustspth);
            FileNameHelper.WriteUInt32ToFile([.._downloadedNovels], downloadednovelspth);
            //end
            isStop = ui = false;
            Console.WriteLine($"[MAIN I] Finished download {_dl} items (total {_downloadedIllusts.Count}I + {_downloadedNovels.Count}N = {_downloadedIllusts.Count + _downloadedNovels.Count} now)");
            //end!
        }
    }
}