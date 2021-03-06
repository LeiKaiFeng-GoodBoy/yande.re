﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using System.Net.Security;
using System.Net.Sockets;
using System.Linq;
using System.Security.Cryptography;
using LeiKaiFeng.Http;
using System.Threading.Channels;
using System.Threading;
using System.Xml.Serialization;
using System.Text;
using System.Text.Json;

namespace yande.re
{

    static class Log
    {
        static readonly object s_lock = new object();

        static void Write_(string name, object obj)
        {
            lock (s_lock)
            {
                string s = System.Environment.NewLine;

                File.AppendAllText($"/storage/emulated/0/pixiv.{name}.txt", $"{s}{s}{s}{s}{DateTime.Now}{s}{obj}", System.Text.Encoding.UTF8);
            }
        }

        public static void Write(string name, object obj)
        {
            Write_(name, obj);
        }

        public static void Write(string name, Task task)
        {
            task.ContinueWith((t) =>
            {
                try
                {
                    t.Wait();
                }
                catch (Exception e)
                {
                    Log.Write(name, e);
                }
            });
        }
    }

    sealed class DeleteRepeatFile
    {
        static string GetStreamHashCode(Stream stream)
        {
            HashAlgorithm hashAlgorithm = SHA256.Create();

            byte[] hashCode = hashAlgorithm.ComputeHash(stream);

            return BitConverter.ToString(hashCode).Replace("-", "");

        }

        static Stream Open(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        }

        static string GetFileHashCode(string path)
        {
            using (var stream = Open(path))
            {
                return GetStreamHashCode(stream);
            }            
        }

        public static void Statr(string folderPath)
        {
            var paths = Directory.EnumerateFiles(folderPath);

            var dic = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in paths)
            {
                string hash = GetFileHashCode(path);
                

                if (dic.TryGetValue(hash, out var list))
                {
                    list.Add(path);
                }
                else
                {
                    list = new List<string>();

                    list.Add(path);

                    dic[hash] = list;
                }
            }


            foreach (var list in dic.Values)
            {

                for (int i = 1; i < list.Count; i++)
                {
                    File.Delete(list[i]);
                }
            }
        }
    }

    public sealed class WebInfo
    {
        public string Key { get; set; }


        public string HtmlDns { get; set; }


        public string HtmlSni { get; set; }

        public string HtmlHost { get; set; }

        
        public string ImgDns { get; set; }

        public string ImgSni { get; set; }

        public string ImgHost { get; set; }

    }

    public sealed class WebInfoHelper
    {
        static bool Is(string s)
        {
            if (s is null)
            {
                return false;
            }

            try
            {
                new Uri($"http://{s.Trim()}/");

                return true;
            }
            catch (UriFormatException)
            {
                return false;
            }
        }

        static bool Is(WebInfo webInfo)
        {
            return Is(webInfo.HtmlDns) &&
                Is(webInfo.HtmlHost) &&
                Is(webInfo.HtmlSni) &&
                Is(webInfo.ImgDns) &&
                Is(webInfo.ImgHost) &&
                Is(webInfo.ImgSni);
        }

        static bool Is(WebInfo[] webInfos, out string message)
        {
            if (webInfos.All((v) => Is(v)))
            {
                if (webInfos.Any((s) => s.Key == GetPagesContent.WebSite.Yandere.ToString()) &&
                    webInfos.Any((s) => s.Key == GetPagesContent.WebSite.Konachan.ToString()))
                {
                    message = "";

                    return true;
                }
                else
                {
                    message = "网站不匹配";

                    return false;
                }
            }
            else
            {
                message = "值不是有效域名";

                return false;
            }
        }

        public static bool TryCreate(string s, out string message)
        {
            try
            {
                if (Is(JsonSerializer.Deserialize<WebInfo[]>(s), out message))
                {
                    
                    return true;
                }
                else
                {
                    return false;
                }



                
            }
            catch (JsonException)
            {
                message = "Json格式错误";
            }
            catch (NotSupportedException)
            {
                message = "类型错误";
            }

            return false;
        }

        public static string CreateInputText()
        {
            return JsonSerializer.Serialize(JsonSerializer.Deserialize<WebInfo[]>(InputData.WebInfo2), new JsonSerializerOptions { WriteIndented = true });
        }
    }


    abstract class GetWebSiteContent
    {

        const string Konachan_Host = "https://konachan.com";

        const string Yandere_Host = "https://yande.re";


        public static string CreateDefInfo()
        {
            return JsonSerializer.Serialize(new WebInfo[] {

                new WebInfo
                {
                    Key=GetWebSiteContent.WebSite.Konachan.ToString(),

                    HtmlDns=new Uri( GetWebSiteContent.Konachan_Host).Host,

                    HtmlSni=new Uri( GetWebSiteContent.Konachan_Host).Host,

                    HtmlHost=new Uri( GetWebSiteContent.Konachan_Host).Host,

                    ImgDns=new Uri( GetWebSiteContent.Konachan_Host).Host,

                    ImgSni=new Uri( GetWebSiteContent.Konachan_Host).Host,

                    ImgHost=new Uri( GetWebSiteContent.Konachan_Host).Host
                },

                new WebInfo
                {
                    Key = GetWebSiteContent.WebSite.Yandere.ToString(),

                    HtmlDns=new Uri( GetWebSiteContent.Konachan_Host).Host,

                    HtmlSni=new Uri( GetWebSiteContent.Yandere_Host).Host,

                    HtmlHost=new Uri( GetWebSiteContent.Yandere_Host).Host,

                    ImgDns=new Uri( GetWebSiteContent.Konachan_Host).Host,

                    ImgSni=new Uri( GetWebSiteContent.Yandere_Host).Host,

                    ImgHost=new Uri( GetWebSiteContent.Yandere_Host).Host
                }
            });
        }


        static void SetWebInfo(WebInfo webInfo, MHttpClientHandler handler, out Uri host)
        {
            handler.StreamCallback = MHttpClientHandler.CreateNewConnectAsync(
                MHttpClientHandler.CreateCreateConnectAsyncFunc(webInfo.HtmlDns, 443),
                MHttpClientHandler.CreateCreateAuthenticateAsyncFunc(webInfo.HtmlSni, false));


            host = new Uri($"https://{webInfo.HtmlHost}/");
        }

        static void SetImgWebInfo(WebInfo webInfo, MHttpClientHandler handler)
        {

            handler.StreamCallback = MHttpClientHandler.CreateNewConnectAsync(
                MHttpClientHandler.CreateCreateConnectAsyncFunc(webInfo.ImgDns, 443),
                MHttpClientHandler.CreateCreateAuthenticateAsyncFunc(webInfo.ImgSni, false));
        }

        static void GetHttpClient(WebSite webSite, int maxSize, int poolCount, out Uri host, out MHttpClient htmlClient, out MHttpClient imgClient)
        {

            

            var v = JsonSerializer.Deserialize<WebInfo[]>(InputData.WebInfo2);

            var wv = v.Where((item) => item.Key == webSite.ToString()).First();

            var imgHandler = new MHttpClientHandler
            {

                MaxResponseContentSize = 1024 * 1024 * maxSize,

                MaxStreamPoolCount = poolCount,

                MaxStreamParallelRequestCount = 4,

                MaxStreamRequestCount = 30,
            };

            SetImgWebInfo(wv, imgHandler);

            imgClient = new MHttpClient(imgHandler);


            var htmlHandle = new MHttpClientHandler();

            SetWebInfo(wv, htmlHandle, out host);

            htmlClient = new MHttpClient(htmlHandle);

        }


        public enum WebSite
        {
            Konachan,
            Yandere
        }

        public enum Popular
        {
            Pages,

            Day,

            Week,

            Month
        }

        readonly Regex m_regex = new Regex(@"<a class=""directlink (?:largeimg|smallimg)"" href=""([^""]+)""");

        readonly MHttpClient m_htmlClient;

        readonly MHttpClient m_imgClient;

        readonly Uri m_host;

        readonly TimeSpan m_timeSpan;

        public GetWebSiteContent(WebSite webSite, TimeSpan timeOut, int maxSize, int poolCount)
        {
            GetHttpClient(webSite, maxSize, poolCount, out m_host, out m_htmlClient, out m_imgClient);

            m_timeSpan = timeOut;
        }

        protected abstract void UpdateViewText();

        protected abstract void MoveNext();

        protected abstract string GetPath();

        Uri GetUri()
        {
            UpdateViewText();

            Uri uri = new Uri(m_host, GetPath());

            MoveNext();

            return uri;
        }

        List<Uri> ParseUris(string html)
        {
            var match = m_regex.Match(html);

            var list = new List<Uri>();

            while (match.Success)
            {
                list.Add(new Uri(match.Groups[1].Value));

                match = match.NextMatch();
            }

            return list;
        }


        public Func<Uri, CancellationToken, Task<byte[]>> CreateGetImageFunc()
        {
            var reTryFunc = CreateReTryFunc<byte[]>();

            var timeOutFunc = CreateTimeOutReTryFunc<byte[]>(m_timeSpan, 9);

            return (uri, tokan) =>
            {
                return reTryFunc((tokan) => timeOutFunc((tokan) => m_imgClient.GetByteArrayAsync(uri, tokan), tokan), tokan);      
            };
        }


        public Func<CancellationToken, Task<List<Uri>>> CreateGetHtmlFunc()
        {
            var reTryFunc = CreateReTryFunc<List<Uri>>();

            var timeOutFunc = CreateTimeOutReTryFunc<List<Uri>>(m_timeSpan, 9);

            var endFunc = CreateHtmlLoadEndFunc();



            return (tokan) =>
            {

                return endFunc((tokan) =>
                {
                    Uri uri = GetUri();

                    Func<CancellationToken, Task<List<Uri>>> func = async (tokan) =>
                    {
                        string html = await m_htmlClient.GetStringAsync(uri, tokan).ConfigureAwait(false);

                        return ParseUris(html);
                    };

                    return reTryFunc((tokan) => timeOutFunc(func, tokan), tokan);
                }, tokan);


            };
        }


        static Func<Func<CancellationToken, Task<T>>, CancellationToken, Task<T>> CreateReTryFunc<T>()
        {
            return async (func, tokan) =>
            {

                while (true)
                {
                    try
                    {
                        return await func(tokan).ConfigureAwait(false);
                    }
                    catch(MHttpClientException e)
                    {
                        if (e.InnerException is SocketException ||
                            e.InnerException is IOException ||
                            e.InnerException is ObjectDisposedException)
                        {

                        }
                        else
                        {
                            throw;
                        }
                    }

                    await Task.Delay(new TimeSpan(0, 0, 2)).ConfigureAwait(false);
                }
            };
        }

        static Func<Func<CancellationToken, Task<T>>, CancellationToken, Task<T>> CreateTimeOutReTryFunc<T>(TimeSpan timeSpan, int maxCount)
        {
            return async (func, tokan) =>
            {
                var timeOut = timeSpan;

                foreach (var item in Enumerable.Range(0, maxCount)) 
                {
                    using (var source = new CancellationTokenSource(timeOut))
                    using (tokan.Register(source.Cancel))
                    {
                        try
                        {
                            return await func(source.Token).ConfigureAwait(false);
                        }
                        catch (MHttpClientException e)
                        {
                            if (e.InnerException is OperationCanceledException)
                            {
                                if (tokan.IsCancellationRequested)
                                {
                                    throw new OperationCanceledException();
                                }
                                else
                                {

                                    timeOut += timeSpan;
                                }

                            }
                            else
                            {
                                throw;
                            }

                            

                        }
                    }
                }

                throw new MHttpClientException(new OperationCanceledException());
            };
        }

        static Func<Func<CancellationToken, Task<List<Uri>>>, CancellationToken, Task<List<Uri>>> CreateHtmlLoadEndFunc()
        {
            int n = 0;

            return async (func, tokan) =>
            {

                while (true)
                {
                   
                    var list = await func(tokan).ConfigureAwait(false);

                    if (list.Count == 0)
                    {
                        n++;

                        if (n >= 3)
                        {
                            throw new ChannelClosedException();
                        }
                    }
                    else
                    {
                        n = 0;

                        return list;
                    }
                }
            };
        }

    }

    sealed class GetPagesContent : GetWebSiteContent
    {

        int m_pages;

        readonly Action<int> m_action;

        readonly string m_tag;

        public GetPagesContent(
            WebSite webSite,
            string tag,
            int pages,
            Action<int> action,
            TimeSpan timeOut,
            int maxSize,
            int poolCount) : base(webSite,
                                timeOut,
                                maxSize,
                                poolCount)
        {

            m_pages = pages;

            m_action = action;

            m_tag = tag.Trim();
        }

        protected override string GetPath()
        {

            if (string.IsNullOrWhiteSpace(m_tag))
            {

                return $"/post?page={m_pages}";

            }
            else
            {

                return $"/post?page={m_pages}&tags={m_tag}";

            }

        }

        protected override void UpdateViewText()
        {
            m_action(m_pages);
        }

        protected override void MoveNext()
        {
            m_pages++;
        }
    }

    sealed class GetDateTimeContent : GetWebSiteContent
    {
        DateTime m_dateTime;

        readonly Action<DateTime> m_action;

        readonly Popular m_popular;

        public GetDateTimeContent(
            WebSite webSite,
            Popular popular,
            DateTime dateTime,
            Action<DateTime> action,
            TimeSpan timeOut,
            int maxSize,
            int poolCount) : base(webSite,
                                timeOut,
                                maxSize,
                                poolCount)
        {

            if (popular == Popular.Pages)
            {
                throw new ArgumentException("应该是按日期");
            }

            m_popular = popular;

            m_dateTime = dateTime;

            m_action = action;
        }

        protected override string GetPath()
        {
            string s;

            DateTime dateTime = m_dateTime;

            if (m_popular == Popular.Day)
            {
                s = $"/post/popular_by_day?day={dateTime.Day}&month={dateTime.Month}&year={dateTime.Year}";

            }
            else if (m_popular == Popular.Week)
            {
                s = $"/post/popular_by_week?day={dateTime.Day}&month={dateTime.Month}&year={dateTime.Year}";

            }
            else
            {
                s = $"/post/popular_by_month?month={dateTime.Month}&year={dateTime.Year}";

            }

            return s;
        }

        protected override void UpdateViewText()
        {
            m_action(m_dateTime);
        }

        protected override void MoveNext()
        {
            TimeSpan timeSpan;

            if (m_popular == Popular.Day)
            {
                timeSpan = new TimeSpan(-1, 0, 0, 0);
            }
            else if (m_popular == Popular.Week)
            {
                timeSpan = new TimeSpan(-7, 0, 0, 0);
            }
            else
            {
                timeSpan = new TimeSpan(-31, 0, 0, 0);

            }

            m_dateTime = m_dateTime.Add(timeSpan);
        }
    }

    public sealed class PreLoad
    {

        static async Task GetUrisTask(Func<CancellationToken, Task<List<Uri>>> func, ChannelWriter<Uri> uris, CancellationToken cancellationToken)
        {
               
            while (true)
            {
                    
                
                try
                {

                    var list = await func(cancellationToken).ConfigureAwait(false);

                    foreach (var item in list)
                    {
                        await uris.WriteAsync(item).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ChannelClosedException)
                {
                    return;
                }
                catch (MHttpClientException)
                {

                }      
            }
        }

        static async Task GetImageTask(Func<Uri, CancellationToken, Task<byte[]>> func, ChannelReader<Uri> uris, ChannelWriter<byte[]> imgs, CancellationToken cancellationToken)
        {
            while (true)
            {

                try
                {
                    Uri uri = await uris.ReadAsync().ConfigureAwait(false);

                    var buffer = await func(uri, cancellationToken).ConfigureAwait(false);


                    await imgs.WriteAsync(buffer).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ChannelClosedException)
                {
                    return;
                }
                catch (MHttpClientException)
                {
                   
                }

            }
        }

        static Action<Task> CreateCencelAction(ChannelWriter<Uri> uris, ChannelWriter<byte[]> imgs, CancellationTokenSource source)
        {
            return (t) =>
            {
                uris.TryComplete();

                imgs.TryComplete();

                source.Cancel();
            };
        }

        static Task AddAllTask(int imgCount, Func<Task> func)
        {

            var list = new List<Task>();

            foreach (var item in Enumerable.Range(0, imgCount))
            {
                list.Add(Task.Run(func));
            }

            return Task.WhenAll(list.ToArray());

        }

        public static PreLoad Create(Func<CancellationToken, Task<List<Uri>>> urisFunc, Func<Uri, CancellationToken, Task<byte[]>> imgsFunc, int uriCount, int imgCount, int taskCount)
        {
            
            var uris = Channel.CreateBounded<Uri>(uriCount);

            var imgs = Channel.CreateBounded<byte[]>(imgCount);

            var cancelSource = new CancellationTokenSource();

            var cancelAction = CreateCencelAction(uris, imgs, cancelSource);


            Task.Run(() => GetUrisTask(urisFunc, uris, cancelSource.Token))
                .ContinueWith(cancelAction);

            AddAllTask(taskCount, () => GetImageTask(imgsFunc, uris, imgs, cancelSource.Token))
                .ContinueWith(cancelAction);

            var v = new PreLoad();


            v.CancelAction = () => cancelAction(Task.CompletedTask);

            v.ReadFunc = () => imgs.Reader.ReadAsync(cancelSource.Token);

            return v;
        }

        Action CancelAction { get; set; }
        

        Func<ValueTask<byte[]>> ReadFunc { get; set; }

        
        public void Cencel()
        {
            CancelAction();
        }

        public Task While(Func<Data, Task<TimeSpan>> func)
        {
            return Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        if (m_slim.Wait(TimeSpan.Zero))
                        {
                            var data = await ReadFunc().ConfigureAwait(false);

                            var timeSpan = await func(new Data(data)).ConfigureAwait(false);

                            await Task.Delay(timeSpan).ConfigureAwait(false);
                        }
                        else
                        {
                            await Task.Delay(new TimeSpan(0, 0, 2)).ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException)
                {

                }
                catch (ChannelClosedException)
                {

                }




            });
        }


        ManualResetEventSlim m_slim = new ManualResetEventSlim();

        public void SetWait()
        {
            m_slim.Reset();
        }

        public void SetNotWait()
        {
            m_slim.Set();
        }

        private PreLoad()
        {
            SetNotWait();
        }
    }

    public sealed class Data
    {
        public Data(byte[] buffer)
        {
            Buffer = buffer;

            ImageSource = ImageSource.FromStream(() => new MemoryStream(Buffer));
        }

        public ImageSource ImageSource { get; }

        public byte[] Buffer { get; }


    }

    static class InputData
    {

        public static string WebInfo2
        {
            get => Preferences.Get(nameof(WebInfo2), GetWebSiteContent.CreateDefInfo());
            set => Preferences.Set(nameof(WebInfo2), value);
        }

        public static string Tag
        {
            get => Preferences.Get(nameof(Tag), string.Empty);
            set => Preferences.Set(nameof(Tag), value);
        }

        public static int Pages
        {
            get => Preferences.Get(nameof(Pages), 1);
            set => Preferences.Set(nameof(Pages), value);
        }

        public static DateTime DateTime
        {
            get => Preferences.Get(nameof(DateTime), DateTime.Today);
            set => Preferences.Set(nameof(DateTime), value);
        }

        public static string Host
        {
            get=> Preferences.Get(nameof(Host), "");

            set=> Preferences.Set(nameof(Host), value);
        }

        public static string Popular
        {
            get => Preferences.Get(nameof(Popular), "");

            set => Preferences.Set(nameof(Popular), value);
        }

        public static int TimeSpan
        {
            get => Preferences.Get(nameof(TimeSpan), 2);
            set => Preferences.Set(nameof(TimeSpan), value);
        }

        public static int MaxSize
        {
            get => Preferences.Get(nameof(MaxSize), 5);
            set => Preferences.Set(nameof(MaxSize), value);
        }

        public static int TaskCount
        {
            get => Preferences.Get(nameof(TaskCount), 1);
            set => Preferences.Set(nameof(TaskCount), value);
        }

        public static int ImgCount
        {
            get => Preferences.Get(nameof(ImgCount), 6);
            set => Preferences.Set(nameof(ImgCount), value);
        }

        public static int TimeOut
        {
            get => Preferences.Get(nameof(TimeOut), 60);
            set => Preferences.Set(nameof(TimeOut), value);
        }

        

        static int F(string s)
        {
            if (int.TryParse(s, out int n) && n >= 0)
            {
                
                return n;
            }
            else
            {
                throw new FormatException();
            }
        }


        public static bool Create(string taskCount, string tag, string timeSpan, string timeOut, string maxSize, string imgCount, string pages, string host, string populat)
        {
            
            try
            {

                TimeSpan = F(timeSpan);

                MaxSize = F(maxSize);

                ImgCount = F(imgCount);

                TaskCount = F(taskCount);

                TimeOut = F(timeOut);

                Pages = F(pages);

                Host = host;

                Popular = populat;

                Tag = tag;

                return true;
            }
            catch (FormatException)
            {
                return false;
            }

        }
    }


    sealed class Awa
    {

        TaskCompletionSource<object> m_source;

        public void SetAwait()
        {
            if (m_source is null)
            {
                m_source = new TaskCompletionSource<object>();
            }
            else
            {

            }
        }

        public void SetAdd()
        {
            if (m_source is null)
            {

            }
            else
            {
                var v = m_source;

                m_source = null;

                v.TrySetResult(default);
            }
        }


        public Task Get()
        {
            if (m_source is null)
            {
                return Task.CompletedTask;
            }
            else
            {
                return m_source.Task;
            }
        }
    }


    public partial class MainPage : ContentPage
    {
        const int COLL_VIEW_COUNT = 6;

        const int URI_LOAD_COUNT = 64;

        const int FLSH_COUNT = 32;

        const string ROOT_PATH = "/storage/emulated/0/konachan_image";

        public MainPage()
        {
            InitializeComponent();

            Task t = InitPermissions();
        }

        async Task InitPermissions()
        {
            var p = await Permissions.RequestAsync<Permissions.StorageWrite>();

            if (p == PermissionStatus.Granted)
            {
                Init();

            }
            else
            {
                Task t = DisplayAlert("错误", "需要存储权限", "确定");
            }
        }

        void Init()
        {
            DeviceDisplay.KeepScreenOn = true;

            InitSelectView();

            InitPopularView();

            SetInput();


        }

        void ChangeInputVisible(GetWebSiteContent.Popular popular)
        {
            if (popular == GetWebSiteContent.Popular.Pages)
            {
                
            }
            else
            {
                
            }
        }

        void SetInput()
        {
            m_tag_value.Text = InputData.Tag;

            m_pages_value.Text = InputData.Pages.ToString();

            m_datetime_value.Date = InputData.DateTime;

            m_timespan_value.Text = InputData.TimeSpan.ToString();

            m_maxsize_value.Text = InputData.MaxSize.ToString();

            m_imgcount_value.Text = InputData.ImgCount.ToString();

            m_task_count_value.Text = InputData.TaskCount.ToString();

            m_timeout_value.Text = InputData.TimeOut.ToString();
        }

        bool CreateInput()
        {
            return InputData.Create(m_task_count_value.Text, m_tag_value.Text, m_timespan_value.Text, m_timeout_value.Text, m_maxsize_value.Text, m_imgcount_value.Text, m_pages_value.Text, m_select_value.SelectedItem.ToString(), m_popular_value.SelectedItem.ToString());
        }

        Action<DateTime> SetDateTime(Label label)
        {

            return (dateTime) =>
            {
                label.Text = dateTime.ToString();

                InputData.DateTime = dateTime;
            };

          

        }


        Action<int> SetPages(Label label)
        {
            return (n) =>
            {

                label.Text = n.ToString();

                InputData.Pages = n;
            };

        }

        void InitPopularView()
        {
            var vs = Enum.GetNames(typeof(GetWebSiteContent.Popular));

            string popular = InputData.Popular;

            int index = vs.ToList().FindIndex((s) => s == popular);

            index = index == -1 ? 0 : index;

            m_popular_value.ItemsSource = vs;

            m_popular_value.SelectedIndex = index;

            OnPopularSelect(m_popular_value, EventArgs.Empty);
        }

        void InitSelectView()
        {
            var vs = Enum.GetNames(typeof(GetWebSiteContent.WebSite));

            string host = InputData.Host;

            int index = vs.ToList().FindIndex((s) => s == host);

            index = index == -1 ? 0 : index;

            m_select_value.ItemsSource = vs;

            m_select_value.SelectedIndex = index;
        }



        
        void OnResetDateTime(object sender, EventArgs e)
        {
            m_datetime_value.Date = DateTime.Today;
        }

        void OnPopularSelect(object sender, EventArgs e)
        {
            ChangeInputVisible((GetWebSiteContent.Popular)Enum.Parse(typeof(GetWebSiteContent.Popular), m_popular_value.SelectedItem.ToString()));
        }


        


        


        Func<Data, Task<TimeSpan>> SetImage(TimeSpan timeSpan, ObservableCollection<Data> coll)
        {
            return (data) =>
            {
                return MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (coll.Count >= COLL_VIEW_COUNT)
                    {
                        var item = coll.Last();

                        coll.Clear();

                        coll.Add(item);

                        Task.Delay(timeSpan).ContinueWith((t) =>
                        {
                            MainThread.InvokeOnMainThreadAsync(() =>
                            {
                                coll.Add(data);
                            });
                        });


                        return timeSpan.Add(timeSpan).Add(timeSpan);
                    }
                    else
                    {
                        coll.Add(data);

                        return timeSpan;
                    }                
                });
            };


            
        }


        


        void Start()
        {

            var info = new ViewImageListPageInfo((lable, coll) =>
            {
                var get = CreateGet(lable);

                var preLoad = PreLoad.Create(get.CreateGetHtmlFunc(), get.CreateGetImageFunc(), URI_LOAD_COUNT, InputData.ImgCount, InputData.TaskCount);

                var task = preLoad.While(SetImage(new TimeSpan(0, 0, InputData.TimeSpan), coll));

                Log.Write("viewTask", task);

                return preLoad;
            }, ROOT_PATH);



            Navigation.PushModalAsync(new ViewImageListPage(info));
        }

        GetWebSiteContent CreateGet(Label label)
        {
            Directory.CreateDirectory(ROOT_PATH);

            var webSite = (GetWebSiteContent.WebSite)Enum.Parse(typeof(GetWebSiteContent.WebSite), InputData.Host);

            var p = (GetWebSiteContent.Popular)Enum.Parse(typeof(GetWebSiteContent.Popular), InputData.Popular);

            if (p == GetWebSiteContent.Popular.Pages)
            {
                int n = InputData.Pages;

                string tag = InputData.Tag;

                var setText = SetPages(label);

                setText(n);

                return new GetPagesContent(
                    webSite,
                    tag,
                    n,
                    (v) => MainThread.BeginInvokeOnMainThread(() => setText(v)),
                    new TimeSpan(0, 0, InputData.TimeOut),
                    InputData.MaxSize,
                    InputData.TaskCount);
            }
            else
            {
                DateTime dateTime = m_datetime_value.Date;

                var setText = SetDateTime(label);

                setText(dateTime);

                return new GetDateTimeContent(
                    webSite,
                    p,
                    dateTime,
                    (d) => MainThread.BeginInvokeOnMainThread(() => setText(d)),
                    new TimeSpan(0, 0, InputData.TimeOut),
                    InputData.MaxSize,
                    InputData.TaskCount);
            }

        }

        void OnStart(object sender, EventArgs e)
        {
            if (CreateInput() == false)
            {
                Task t = DisplayAlert("错误", "Input Error", "确定");
            }
            else
            {
                Start();
            }      
        }

        void OnSetWebInfo(object sender, EventArgs e)
        {
            Navigation.PushModalAsync(new SettingWebInfoPage());
        }
    }
}