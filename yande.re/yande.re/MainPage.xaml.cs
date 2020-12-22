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

namespace yande.re
{

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
                string hash;
                try
                {
                    hash = GetFileHashCode(path);
                }
                catch (Exception e)
                {
                    
                    continue;
                }

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

    sealed class Http
    {
        public enum WebSite
        {
            Konachan,
            Yandere
        }

        public enum Popular
        {
            Day,

            Week,

            Month
        }

        const string Konachan_Host = "https://konachan.com";

        const string Yandere_Host = "https://yande.re";


        readonly Regex m_regex = new Regex(@"<a class=""directlink largeimg"" href=""([^""]+)""");

        readonly MHttpClient m_request;

        readonly Uri m_host;

        readonly Action<DateTime> m_action;

        readonly Http.Popular m_popular;

        DateTime m_datetime;

        public Http(WebSite webSite, Popular popular, DateTime dateTime, Action<DateTime> action, TimeSpan timeOut, int maxSize, int poolCount)
        {
            m_request = GetHttpClient(webSite, maxSize, poolCount);

            m_request.ResponseTimeOut = timeOut;

            m_host = GetHost(webSite);

            m_datetime = dateTime;

            m_popular = popular;

            m_action = action;

        }

        void SubDateTime()
        {
            TimeSpan timeSpan;

            if(m_popular == Popular.Day)
            {
                timeSpan = new TimeSpan(-1, 0, 0, 0);
            }
            else if(m_popular == Popular.Week)
            {
                timeSpan = new TimeSpan(-7, 0, 0, 0);
            }
            else
            {
                timeSpan = new TimeSpan(-31, 0, 0, 0);

            }

            m_datetime = m_datetime.Add(timeSpan);
        }

        static Task CreateConnectAsync(Socket socket, Uri uri)
        {
            return socket.ConnectAsync(new Uri(Konachan_Host).Host, 443);
        }

        static async Task<Stream> CreateAuthenticateAsync(Stream stream, Uri uri)
        {
            SslStream sslStream = new SslStream(stream, false);

            await sslStream.AuthenticateAsClientAsync(uri.Host).ConfigureAwait(false);

            return sslStream;
        }

        static MHttpClient GetHttpClient(WebSite webSite, int maxSize, int poolCount)
        {
            int n = poolCount;
           
            poolCount *= 2;

            if (poolCount <= 0)
            {
                poolCount = n;
            }

            var handler = new MHttpClientHandler
            {
                
                MaxResponseSize = 1024 * 1024 * maxSize,

                MaxStreamPoolCount = poolCount
            };



            if (webSite == WebSite.Konachan)
            {
                return new MHttpClient(handler);
            }
            else
            {
                handler.ConnectCallback = CreateConnectAsync;

                handler.AuthenticateCallback = CreateAuthenticateAsync;


                return new MHttpClient(handler);
            }
        }

        static Uri GetHost(WebSite webSite)
        {
            if(webSite == WebSite.Yandere)
            {
                return new Uri(Yandere_Host);
            }
            else
            {
                return new Uri(Konachan_Host);
            }
             
        }


        Uri GetUriPath(DateTime dateTime)
        {
            string s;
           
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


            
            return new Uri(m_host, s);
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


        public Task<byte[]> GetImageBytesAsync(Uri uri)
        {
            return m_request.GetByteArrayAsync(uri);
        }

        public async Task<List<Uri>> GetUrisAsync()
        {
            
            Uri uri = GetUriPath(m_datetime);

            
            string html = await m_request.GetStringAsync(uri).ConfigureAwait(false);

            m_action(m_datetime);

            SubDateTime();


            return ParseUris(html);
        }

    }

    sealed class CreateColl
    {

        static async void GetUris(Http get_content, MyChannels<Task<Uri>> uris)
        {
            while (true)
            {
                try
                {

                    var list = await get_content.GetUrisAsync().ConfigureAwait(false);

                    foreach (var item in list)
                    {
                        await uris.WriteAsync(Task.FromResult(item)).ConfigureAwait(false);
                    }
                }
                catch(Exception e)
                {
                    await uris.WriteAsync((Task.FromException<Uri>(e))).ConfigureAwait(false);
                }

            }
        }
     
        static async void GetImage(Http get_content, MyChannels<Task<Uri>> uris, MyChannels<Task<byte[]>> imgs)
        {
            while (true)
            {

                
                try
                {
                    Uri uri = await (await uris.ReadAsync().ConfigureAwait(false)).ConfigureAwait(false);

                    byte[] buffer = await get_content.GetImageBytesAsync(uri).ConfigureAwait(false);


                    await imgs.WriteAsync((Task.FromResult(buffer))).ConfigureAwait(false);
                }
                catch(Exception e)
                {
                    await imgs.WriteAsync((Task.FromException<byte[]>(e))).ConfigureAwait(false);
                }
            }
        }

        static void GetImage(Http get_content, MyChannels<Task<Uri>> uris, MyChannels<Task<byte[]>> imgs, int imgCount)
        {
            foreach (var item in Enumerable.Range(0, imgCount))
            {
                Task.Run(() => GetImage(get_content, uris, imgs));
            }
        }

        public static MyChannels<Task<byte[]>> Create(Http get_content, int uriCount, int imgCount)
        {
            var uris = new MyChannels<Task<Uri>>(uriCount);

            var imgs = new MyChannels<Task<byte[]>>(imgCount);


            Task.Run(() => GetUris(get_content, uris));


            Task.Run(() => GetImage(get_content, uris, imgs, imgCount));

            return imgs;
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


        public static bool Create(string timeSpan, string timeOut, string maxSize, string imgCount)
        {
            
            try
            {

                TimeSpan = F(timeSpan);

                MaxSize = F(maxSize);

                ImgCount = F(imgCount);

                TimeOut = F(timeOut);

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
        const int COLL_VIEW_COUNT = 32;

        const int URI_LOAD_COUNT = 64;

        const string ROOT_PATH = "/storage/emulated/0/konachan_image";

        readonly ObservableCollection<Data> m_source = new ObservableCollection<Data>();

        readonly Awa m_awa = new Awa();

        public MainPage()
        {
            InitializeComponent();

            InitPermissions();
        }

        void Init()
        {
            DeviceDisplay.KeepScreenOn = true;

            InitSelectView();

            InitPopularView();

            SetInput();


            SetViewImageSource();



        }

        async void InitPermissions()
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

        void WriteLog(Exception e)
        {
            string s = System.Environment.NewLine;

            File.AppendAllText($"/storage/emulated/0/yande.re.exception.txt", $"{s}{s}{s}{s}{DateTime.Now}{s}{e}", System.Text.Encoding.UTF8);
        }

        void SetInput()
        {
            m_datetime_value.Date = InputData.DateTime;

            m_timespan_value.Text = InputData.TimeSpan.ToString();

            m_maxsize_value.Text = InputData.MaxSize.ToString();

            m_imgcount_value.Text = InputData.ImgCount.ToString();

            m_timeout_value.Text = InputData.TimeOut.ToString();
        }

        bool CreateInput()
        {
            return InputData.Create(m_timespan_value.Text, m_timeout_value.Text, m_maxsize_value.Text, m_imgcount_value.Text);
        }

        void SetDateTime(DateTime dateTime)
        {
            
            m_pagesText.Text = dateTime.ToString();

            InputData.DateTime = dateTime;

        }

        void InitPopularView()
        {
            var vs = Enum.GetNames(typeof(Http.Popular));

            string popular = InputData.Popular;

            int index = vs.ToList().FindIndex((s) => s == popular);

            index = index == -1 ? 0 : index;

            m_popular_value.ItemsSource = vs;

            m_popular_value.SelectedIndex = index;
        }

        void InitSelectView()
        {
            var vs = Enum.GetNames(typeof(Http.WebSite));

            string host = InputData.Host;

            int index = vs.ToList().FindIndex((s) => s == host);

            index = index == -1 ? 0 : index;

            m_select_value.ItemsSource = vs;

            m_select_value.SelectedIndex = index;
        }

        

        async Task FlushView()
        {
            await Task.Yield();
        }

        Task SetImage(byte[] buffer)
        {
            var date = new Data(buffer);

            if(m_source.Count >= COLL_VIEW_COUNT)
            {
                m_source.Clear();
            }

            m_source.Add(date);

            m_view.ScrollTo(m_source.Count - 1, position: ScrollToPosition.End, animate: false);

            return FlushView();
        }

        static async Task SaveImage(byte[] buffer)
        {
            try
            {

                string name = Path.Combine(ROOT_PATH, Path.GetRandomFileName() + ".png");

                using (var file = new FileStream(name, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
                {

                    await file.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                }
            }
            catch(Exception e)
            {
                
            }
        }


        void OnCollectionViewSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if(m_view.SelectedItem != null)
            {
                
                Task t = SaveImage(((Data)m_view.SelectedItem).Buffer);

                m_view.SelectedItem = null;
            }
        }

        void SetViewImageSource()
        {
            
            m_view.ItemsSource = m_source;

        }

        async void While(MyChannels<Task<byte[]>> imgs, int timeSpan)
        {
            while (true)
            {
                if (imgs.TryRead(out var item))
                {
                    try
                    {
                        await m_awa.Get();

                        await SetImage(await item);

                        await Task.Delay(timeSpan * 1000);
                    }
                    catch (Exception e)
                    {
                        WriteLog(e);
                    }

                }
                else
                {
                    await Task.Delay(timeSpan * 1000);
                }


            }
        }


        void Start(Http.WebSite webSite, Http.Popular popular, DateTime dateTime)
        {

            if (CreateInput() == false) 
            {
                Task t = DisplayAlert("错误", "Input Error", "确定");

                return;
            }

            var http = new Http(
                webSite,
                popular,
                dateTime,
                (d) => MainThread.BeginInvokeOnMainThread(()=> SetDateTime(d)),
                new TimeSpan(0, 0, InputData.TimeOut),
                InputData.MaxSize, InputData.ImgCount);

            var imgs = CreateColl.Create(http, URI_LOAD_COUNT, InputData.ImgCount);


            int timeSpan = InputData.TimeSpan;

            While(imgs, timeSpan);
        }

        async void OnDeleteFile(object sender, EventArgs e)
        {
            Button button = (Button)sender;

            button.IsEnabled = false;

            try
            {
                await Task.Run(() => DeleteRepeatFile.Statr(ROOT_PATH));


                Task t = DisplayAlert("消息", "Delete 完成", "确定");
            }
            catch
            {
                Task t = DisplayAlert("错误", "Delete error", "确定");
            }
            finally
            {

                button.IsEnabled = true;

            }
        }

        void OnStart(object sender, EventArgs e)
        {
            m_cons.IsVisible = false;

            string host = m_select_value.SelectedItem.ToString();

            string popular = m_popular_value.SelectedItem.ToString();

            DateTime dateTime = m_datetime_value.Date;

            SetDateTime(dateTime);

            InputData.Host = host;

            InputData.Popular = popular;

            Directory.CreateDirectory(ROOT_PATH);


            var webSize = (Http.WebSite)Enum.Parse(typeof(Http.WebSite), host);

            var p = (Http.Popular)Enum.Parse(typeof(Http.Popular), popular);

            Start(webSize, p, dateTime);
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

        void OnResetDateTime(object sender, EventArgs e)
        {
            m_datetime_value.Date = DateTime.Today;
        }
    }
}