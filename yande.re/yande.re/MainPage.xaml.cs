using System;
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


    [Serializable]
    public sealed class WebInfo
    {
        public string Key { get; set; }


        public string DnsHost { get; set; }


        public string SniHost { get; set; }

        public string HostHost { get; set; }

        
    }

    static class Xml
    {
        public static T GetDeserializeXml<T>(string xml)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(T));



            return (T)serializer.Deserialize(new MemoryStream(Encoding.UTF8.GetBytes(xml)));


        }


        public static string GetSerializeXml<T>(T obj)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(T));


            MemoryStream memoryStream = new MemoryStream();


            serializer.Serialize(memoryStream, obj);



            return Encoding.UTF8.GetString(memoryStream.GetBuffer(), 0, checked((int)memoryStream.Position));
        }
    }


    abstract class GetWebSiteContent
    {

        const string Konachan_Host = "https://konachan.com";

        const string Yandere_Host = "https://yande.re";


        public static string CreateDefInfo()
        {
            return Xml.GetSerializeXml(new WebInfo[] {

                new WebInfo
                {
                    Key=GetWebSiteContent.WebSite.Konachan.ToString(),

                    DnsHost=new Uri( GetWebSiteContent.Konachan_Host).Host,

                    SniHost=new Uri( GetWebSiteContent.Konachan_Host).Host,

                    HostHost=new Uri( GetWebSiteContent.Konachan_Host).Host
                },

                new WebInfo
                {
                    Key = GetWebSiteContent.WebSite.Yandere.ToString(),

                    DnsHost=new Uri( GetWebSiteContent.Konachan_Host).Host,

                    SniHost=new Uri( GetWebSiteContent.Yandere_Host).Host,

                    HostHost=new Uri( GetWebSiteContent.Yandere_Host).Host
                }
            });
        }


        static void SetWebInfo(WebInfo webInfo, MHttpClientHandler handler, out Uri host)
        {
            handler.ConnectCallback = (socket, uri) => Task.Run(() => socket.Connect(webInfo.DnsHost, 443));


            handler.AuthenticateCallback = async (stream, uri) =>
            {
                SslStream sslStream = new SslStream(stream, false);

                await sslStream.AuthenticateAsClientAsync(webInfo.SniHost).ConfigureAwait(false);

                return sslStream;
            };


            host = new Uri($"https://{webInfo.HostHost}/");
        }

        static MHttpClient GetHttpClient(WebSite webSite, int maxSize, int poolCount, out Uri host)
        {

            var handler = new MHttpClientHandler
            {

                MaxResponseSize = 1024 * 1024 * maxSize,

                MaxStreamPoolCount = poolCount
            };

            var v = Xml.GetDeserializeXml<WebInfo[]>(InputData.WebInfo);

            var wv = v.Where((item) => item.Key == webSite.ToString()).First();

            SetWebInfo(wv, handler, out host);

            return new MHttpClient(handler);
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

        readonly MHttpClient m_request;

        readonly Uri m_host;

        readonly TimeSpan m_timeSpan;

        public GetWebSiteContent(WebSite webSite, TimeSpan timeOut, int maxSize, int poolCount)
        {
            m_request = GetHttpClient(webSite, maxSize, poolCount, out m_host);

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
                return reTryFunc((tokan) => timeOutFunc((tokan) => m_request.GetByteArrayAsync(uri, tokan), tokan), tokan);      
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
                        string html = await m_request.GetStringAsync(uri, tokan).ConfigureAwait(false);

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
                            if (tokan.IsCancellationRequested)
                            {
                                throw;
                            }
                            else
                            {
                                if (e.InnerException is OperationCanceledException)
                                {
                                    timeOut += timeSpan;
                                }
                                else
                                {
                                    throw;
                                }
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

    sealed class PreLoad
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

            v.ReadFunc = () => Task.Run(() => imgs.Reader.ReadAsync(cancelSource.Token).AsTask());

            return v;
        }

        Action CancelAction { get; set; }
        

        Func<Task<byte[]>> ReadFunc { get; set; }

        private PreLoad()
        {

        }

        public Task<byte[]> ReadAsync()
        {
            return ReadFunc();
        }

        public void Cencel()
        {
            CancelAction();
        }
    }

    sealed class Data
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

        public static string WebInfo
        {
            get => Preferences.Get(nameof(WebInfo), GetWebSiteContent.CreateDefInfo());
            set => Preferences.Set(nameof(WebInfo), value);
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

        readonly ObservableCollection<Data> m_source = new ObservableCollection<Data>();

        readonly Awa m_awa = new Awa();

        Task m_viewTask;

        PreLoad m_preLoad;

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

            InitChangeWebInfoSelectView();

            SetInput();



            SetViewImageSource();



        }

        void ChangeInputVisible(GetWebSiteContent.Popular popular)
        {
            if (popular == GetWebSiteContent.Popular.Pages)
            {
                m_datetime_value_father.IsVisible = false;

                m_pages_value_father.IsVisible = true;
            }
            else
            {
                m_datetime_value_father.IsVisible = true;

                m_pages_value_father.IsVisible = false;
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

        void SetDateTime(DateTime dateTime)
        {
            
            m_pagesText.Text = dateTime.ToString();

            InputData.DateTime = dateTime;

        }


        void SetPages(int n)
        {
            m_pagesText.Text = n.ToString();

            InputData.Pages = n;
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



        void OnCollectionViewSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (m_view.SelectedItem != null)
            {

                Task t = SaveImage(((Data)m_view.SelectedItem).Buffer);

                m_view.SelectedItem = null;
            }
        }

        void SetViewImageSource()
        {

            m_view.ItemsSource = m_source;


        }


        void OnResetDateTime(object sender, EventArgs e)
        {
            m_datetime_value.Date = DateTime.Today;
        }

        void OnPopularSelect(object sender, EventArgs e)
        {
            ChangeInputVisible((GetWebSiteContent.Popular)Enum.Parse(typeof(GetWebSiteContent.Popular), m_popular_value.SelectedItem.ToString()));
        }


        void OnScrolled(object sender, ItemsViewScrolledEventArgs e)
        {
            long n = (long)e.VerticalDelta;

            if (n != 0)
            {
                if (n < 0)
                {
                    m_awa.SetAwait();
                }
                else if (n > 0 && e.LastVisibleItemIndex + 1 == m_source.Count)
                {
                    m_awa.SetAdd();
                }
            }
        }


        static async Task SaveImage(byte[] buffer)
        {
            string name = Path.Combine(ROOT_PATH, Path.GetRandomFileName() + ".png");

            using (var file = new FileStream(name, FileMode.Create, FileAccess.Write, FileShare.None, 1, true))
            {

                await file.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            }
        }




        void SetImage(byte[] buffer)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (m_source.Count >= COLL_VIEW_COUNT)
                {
                    m_source.RemoveAt(0);
                }
            });


            MainThread.BeginInvokeOnMainThread(() =>
            {


                m_source.Add(new Data(buffer));

            });
        }


        async Task While(PreLoad preLoad, int timeSpan)
        {
            try
            {
                while (true)
                {
                    await m_awa.Get();

                    var item = await preLoad.ReadAsync();

                    SetImage(item);

                    await Task.Delay(timeSpan * 1000);
                }
            }
            catch (ChannelClosedException)
            {

            }
            catch (OperationCanceledException)
            {

            }

            
        }


        void Start(GetWebSiteContent get)
        {


            m_preLoad = PreLoad.Create(get.CreateGetHtmlFunc(), get.CreateGetImageFunc(), URI_LOAD_COUNT, InputData.ImgCount, InputData.TaskCount);


            
            m_viewTask = While(m_preLoad, InputData.TimeSpan);

            Log.Write("viewTask", m_viewTask);
        }

        void OnStart(object sender, EventArgs e)
        {
            if (CreateInput() == false)
            {
                Task t = DisplayAlert("错误", "Input Error", "确定");

                return;
            }

            m_cons.IsVisible = false;

            m_viewCons.IsVisible = true;

            Directory.CreateDirectory(ROOT_PATH);

            var webSite = (GetWebSiteContent.WebSite)Enum.Parse(typeof(GetWebSiteContent.WebSite), InputData.Host);

            var p = (GetWebSiteContent.Popular)Enum.Parse(typeof(GetWebSiteContent.Popular), InputData.Popular);

            if(p == GetWebSiteContent.Popular.Pages)
            {
                int n = InputData.Pages;

                string tag = InputData.Tag;

                SetPages(n);

                Start(new GetPagesContent(
                    webSite,
                    tag,
                    n,
                    (v) => MainThread.BeginInvokeOnMainThread(() => SetPages(v)),
                    new TimeSpan(0, 0, InputData.TimeOut),
                    InputData.MaxSize,
                    InputData.TaskCount));
            }
            else
            {
                DateTime dateTime = m_datetime_value.Date;

                SetDateTime(dateTime);

                Start(new GetDateTimeContent(
                    webSite,
                    p,
                    dateTime,
                    (d) => MainThread.BeginInvokeOnMainThread(() => SetDateTime(d)),
                    new TimeSpan(0, 0, InputData.TimeOut),
                    InputData.MaxSize,
                    InputData.TaskCount));
            }

        }

        protected override bool OnBackButtonPressed()
        {
            if (m_preLoad is null || m_viewTask is null)
            {

            }
            else
            {
                var t = m_viewTask;

                m_viewTask = null;

                var p = m_preLoad;

                m_preLoad = null;


                t.ContinueWith((tt) =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {

                        m_source.Clear();

                        m_cons.IsVisible = true;

                        m_viewCons.IsVisible = false;

                        SetInput();

                    });

                });

                p.Cencel();

                DisplayAlert("消息", "正在取消", "确定");
            }

            return true;
        }

        void OnNotViewWebInfo(object sender, EventArgs e)
        {
            m_inputInfo.IsVisible = false;

            m_cons.IsVisible = true;
        }

        void InitChangeWebInfoSelectView()
        {
            var vs = Enum.GetNames(typeof(GetWebSiteContent.WebSite));

            m_web_info_select_value.ItemsSource = vs;

            m_web_info_select_value.SelectedIndex = 0;

            OnChangeWebInfoSelect(m_web_info_select_value, EventArgs.Empty);
        }

        void OnChangeWebInfoSelect(object sender, EventArgs e)
        {
            var v = Xml.GetDeserializeXml<WebInfo[]>(InputData.WebInfo);

            string s = m_web_info_select_value.SelectedItem.ToString();


            SetWebInfoValue(v.Where((item) => item.Key == s).First());
        }


        void SetWebInfoValue(WebInfo webInfo)
        {
            m_dns_host_value.Text = webInfo.DnsHost;

            m_sni_host_value.Text = webInfo.SniHost;

            m_host_host_value.Text = webInfo.HostHost;
        }

        void OnChangeWebInfo(object sender, EventArgs e)
        {



            try
            {
                var v = Xml.GetDeserializeXml<WebInfo[]>(InputData.WebInfo);

                string s = m_web_info_select_value.SelectedItem.ToString();


                GetWebInfoValue(v.Where((item) => item.Key == s).First());


                InputData.WebInfo = Xml.GetSerializeXml(v);

                DisplayAlert("消息", "更改成功", "确定");
            }
            catch (FormatException)
            {
                DisplayAlert("错误", "uri格式不正确", "确定");
            }
        }


        static string AsValue(string s)
        {
            s = s ?? "";

            s = s.Trim();

            return new Uri($"https://{s}/").Host;
        }

        void GetWebInfoValue(WebInfo webInfo)
        {
            webInfo.DnsHost = AsValue(m_dns_host_value.Text);

            webInfo.SniHost = AsValue(m_sni_host_value.Text);

            webInfo.HostHost = AsValue(m_host_host_value.Text);
        }
    }
}