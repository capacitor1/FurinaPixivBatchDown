# FurinaPixivBatchDown

Pixiv 自动化批量下载、更新器

## 说明

与其他现成的项目相比，FurinaPixivBatchDown是专门为**自动化下载和更新**Pixiv用户的所有图片、小说而写的，可以实现自动化运行、下载和更新本地的pixiv文件夹。

现已支持完全自动化运行。

## 使用

> [!IMPORTANT]
> 
> 在程序运行之前，请新建一个空文件，放置位置遵循配置文件的`autoloaduserslist`。该文件用于存储作者ID。

只需要双击exe主程序，直接和命令行进行交互来操作。添加作者后，即可离开电脑，程序将会按照设定自动运行。

输入`Help`来查看通用帮助，比如：

```
[TIPS I] Input Pixiv user id or link to add users.
[TIPS I] Input 'UpdateNow' to start task immediately.
[TIPS I] Input 'Exit' to exit app after save all data.
[TIPS I] Input 'Stop' to stop task and save all data.
[TIPS I] Input 'ListAll' to list all users.
[TIPS I] Input 'Novels' to toggle novels download.
```

### 添加作者

只需要在控制台中粘贴完整Pixiv User的URL链接，回车即可。

看到

```
[MAIN I] Added user '123456'
```

代表添加完成。

### 其他命令

如Help所示。

- 其中，由于程序将在运行后先等待`updateinterval`毫秒才开始运行，如果是初次运行想要立即下载，请输入`udpatenow`。

- 在运行过程中，输入`stop`可停止当前运行的更新。输入`exit`将会先停止运行更新再退出程序。

- 由于作者ID已编码为二进制文件，故如果想要导出可读的作者ID列表，请使用`listall`查看。

- 默认每次更新都会更新小说。这会占用更长的运行时间，且多数情况下小说不怎么自更新。如果要跳过小说的下载，输入`novels`即可。再次输入可重新启用小说下载。

### 配置文件

主程序在上述默认情况下，会生成`Config.json`配置文件，其示例和用法如下：

```json5
{
  "cookie": "PHPSESSID=xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
  //你的 pixiv cookie，只需要PHPSESSID

  "savebasepath": "D:\\Files\\Pixiv",
  //下载的文件保存位置（不填为exe同目录）

  "needAI": true,
  //是否需要AI作品

  "autoloaduserslist" : "D:\\PixivUsersUpdateList.bytes",
  //用户列表

  "init_429delay" : 30000,
  //如果发生429错误的重试间隔（初始值）

  "apirequestdelay" : 1000,
  //每两次API请求最小间隔（防止过早出现429）
  
  "updateinterval" : 172800000
  //程序自动运行时，每两次更新之间的等待间隔（单位毫秒）
}
```

> 注：此示例写成json5是为了方便加注释，此程序不支持读取json5！

配置好文件后，将`Config.json`保存到exe同目录下，然后双击程序，即可加载该配置。

## 特性

FurinaPixivBatchDown相比其他类型Pixiv下载器，对于实际使用情况做了一些优化：

- 具备断网重试+无限重试功能，以应对某些VPN节点速度慢、不稳定、经常爆炸的问题。程序会自动无限重试请求，直到下载完毕。**图片文件下载支持断点续传。** 但如果遇到404错误，则会记录到日志并且跳过该文件。

- 支持保存一切原始数据。这有助于本地文件夹为其他程序提供服务提供可能性，但会使磁盘空间被额外占用。对于ugoira动画，保存原始zip文件而不是转换为MJPG或APNG格式，这不是缺陷。

- （伪）多线程下载。使用丢弃符而不是await执行下载方法，可以做到类似多线程的下载模式，极大提升速度。**使用Semaphore信号控制并发数，避免TCP连接过多。** 

- 应对Pixiv 429问题，支持**指数退避** ：初始等待间隔由配置文件设置（默认30000ms），后续如果触发一次429，则该时长乘以2（如，第一次等待30000ms，第二次60000ms，第三次120000ms...），直到Pixiv返回200。

- 一部分需要重复获取、保存的JSON（如小说更新），不会覆盖旧的，而是将旧的**压缩归档**后存储新的。这有助于进行“备份”操作，避免有些情况下作者因不可抗力将已有小说“打码”或者“删减”。

但是，目前FurinaPixivBatchDown确实还有一些缺陷：

- 没有对本地**图片**文件进行校验的能力，故如果遇到已下载过的图片被作者换源，则无法检测并更新。

- 配置文件可以决定是否下载AI作品，但不能决定是否下载R-18(G)作品，将全部下载（根据cookie对应账户的设置情况），且无法自动分类文件夹。

