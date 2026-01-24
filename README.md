# FurinaPixivBatchDown

Pixiv （半）自动化批量下载、更新器

程序运行示例：

```plain
[CONF I] Loading config...
[CONF I] Config loaded successfully.
[MAIN I] Processed 61 lines and gathered 61 users.
...
[API  I] /user/102824301/profile/all : OK
[API  I] /user/102824301?full=1 : OK
[DOWN I] 'avatar.png'(39128B) finished
[DOWN I] 'bg.png'(46434B) finished
[MAIN I] Get 37 works and 0 novels from 102824301.
[API  I] /illust/138711214?time=1767452686133 : OK
[DOWN I] '138711214_p0.jpg'(6082024B) finished
[API  I] /illust/138597841?time=1767452687695 : OK
[DOWN I] '138597841_p0.jpg'(6354025B) finished
[API  I] /illust/138480383?time=1767452689263 : OK
[DOWN I] '138480383_p0.jpg'(2298983B) finished
...
[API  I] /illust/70484875?time=1767453950555 : OK
[MAIN I] API requests finished,but remaining 1 files are downloading
[DOWN I] '70484875_p0.jpg'(824386B) finished
[MAIN I] Done!
[MAIN I] Press any key to exit.
```

## 说明

与其他现成的项目相比，FurinaPixivBatchDown是专门为**自动化下载和更新**Pixiv用户的所有图片、小说而写的，可以实现自动化运行、下载和更新本地的pixiv文件夹。

> *为什么是（半）自动化：FurinaPixivBatchDown本身没有定时任务之类功能，所以真正的全自动化必须配合[Windows计划任务](https://zh.wikipedia.org/wiki/Windows%E5%B7%A5%E4%BD%9C%E6%8E%92%E7%A8%8B%E5%99%A8)或者修改代码使其循环定时/定间隔执行。

## 使用

### 默认：

只需要双击exe主程序，在程序提示`Input pixiv users URL(or path to users list):`时，输入待下载的用户URL。

支持直接输入类似`https://www.pixiv.net/en/users/16064899`的链接（支持不同的语言，或意外带有URL Params的链接），

以及输入包含大量链接的文本文档：`D:\PixivUsersUpdateList.txt`：

`D:\PixivUsersUpdateList.txt`可用的示例：

```plain
https://www.pixiv.net/en/users/16064899
https://www.pixiv.net/users/297323
https://www.pixiv.net/en/users/66477791
这一行不是链接
https://www.pixiv.net/en/users/5657073
https://www.pixiv.net/en/users/102824301/
https://www.example.com/thisisnotpixiv
https://www.pixiv.net/en/users/12990124?params=xxx

```

> 支持包含空行、非HTTP行、非user链接行，但不支持仅输入ID。

### 带有配置文件：

主程序在上述默认情况下，会生成`Config.json`配置文件，其示例和用法如下：

```json5
{
  "cookie": "PHPSESSID=xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
  //你的 pixiv cookie，只需要PHPSESSID

  "savebasepath": "D:\\Files\\Pixiv",
  //下载的文件保存位置（不填既为exe同目录下）

  "needAI": true,
  //是否需要AI作品

  "autoloaduserslist" : "D:\\PixivUsersUpdateList.txt",
  //自动加载用户列表（不填为手动输入）

  "init_429delay" : 30000,
  //如果发生429错误的重试间隔（初始值）

  "apirequestdelay" : 1000,
  //每两次API请求最小间隔（防止过早出现429）

  "needupdatenovels" : false
  //是否需要更新小说（手动指定，用于更新小说）
}
```

> 注：此示例写成json5是为了方便加注释，此程序不支持读取json5！

配置好文件后，将`Config.json`保存到exe同目录下，然后双击程序，即可加载该配置。

### 自动下载和计划任务：

在上述`Config.json`配置文件中，设置可用的`autoloaduserslist`路径即可。

对于计划任务，有多种方法可以进行设置，但不建议将间隔设置的太小（作者更新也需要时间、列表过长时或恰好有大量图片需要下载时，FurinaPixivBatchDown完整运行一次也需要时间）。

FurinaPixivBatchDown每次启动会读取一遍`autoloaduserslist`的文件，然后释放文件。可以配合其他自动程序（比如Web后端），远程更新该list列表，以在程序下一次启动之后，进行更新。

## 特性

FurinaPixivBatchDown相比其他类型Pixiv下载器，对于实际使用情况做了一些优化：

- 具备断网重试+无限重试功能，以应对某些VPN节点速度慢、不稳定、经常爆炸的问题。程序会自动无限重试请求，直到下载完毕。但如果遇到404错误，则会记录到日志并且跳过该文件。

- 支持保存一切原始数据。这有助于本地文件夹为其他程序提供服务提供可能性，但会使磁盘空间被占用。对于ugoira动画，保存原始zip文件而不是转换为MJPG或APNG格式，这不是缺陷。

- （伪）多线程下载。使用丢弃符而不是await执行下载方法，可以做到类似多线程的下载模式，极大提升速度。*但此方法在网络出现阻断或网速过慢时导致线程积累而堵塞（表现为同时下载极大量文件但又都下不完）。*

- 应对Pixiv 429问题，采取增量累计的方式，阻塞进程进行等待：初始等待间隔由配置文件设置（默认30000ms），后续如果触发一次429，则该时长乘以2（如，第一次等待30000ms，第二次60000ms，第三次120000ms...），直到Pixiv返回200。

- 一部分需要重复获取、保存的JSON（如小说更新），不会覆盖旧的，而是将旧的标记后存储新的。这有助于进行“备份”操作，并且避免有些情况下作者因不可抗力将已有小说“打码”或者“删减”。

但是，目前FurinaPixivBatchDown确实还有一些缺陷：

- 没有对本地文件进行校验的能力，故如果遇到已下载过的图片被作者换源，则无法检测并更新。

- 配置文件可以决定是否下载AI作品，但不能决定是否下载R-18(G)作品，将全部下载（根据cookie对应账户的设置情况），且无法自动分类文件夹。

