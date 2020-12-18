using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Security.Cryptography;
using System.Collections.Concurrent;
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
        const string Konachan_Host = "https://konachan.com";

        const string Yandere_Host = "https://yande.re";


        readonly Regex m_regex = new Regex(@"<a class=""directlink largeimg"" href=""([^""]+)""");

        readonly MHttpClient m_request;

        readonly Uri m_host;

        readonly Func<Task<DateTime>> m_nextPagesFunc;

        public Http(string host, Func<Task<DateTime>> nextPagesFunc, TimeSpan timeOut, int maxSize, int poolCount)
        {
            m_request = GetHttpClient(host, maxSize, poolCount);

            m_request.TimeOut = timeOut;

            m_host = GetHost(host);


            m_nextPagesFunc = nextPagesFunc;

        }

        public static string[] GetSource()
        {
            return new string[]
            {
                Yandere_Host,
                Konachan_Host
            };
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

        static MHttpClient GetHttpClient(string host, int maxSize, int poolCount)
        {
            if(host == Konachan_Host)
            {
                return new MHttpClient();
            }
            else
            {
                int n = poolCount;
                poolCount *= 2;

                if (poolCount <= 0)
                {
                    poolCount = n;
                }

                return new MHttpClient(new MHttpClientHandler
                {
                    ConnectCallback = CreateConnectAsync,

                    AuthenticateCallback = CreateAuthenticateAsync,

                    MaxResponseSize = 1024 * 1024 * maxSize,

                    MaxStreamPoolCount = poolCount
                });
            }
        }

        static Uri GetHost(string host)
        {
            if(host == null)
            {
                return new Uri(Yandere_Host);
            }
            else
            {
                string v = GetSource().ToList().Find((s) => s == host);

                if(v == null)
                {
                    return new Uri(Yandere_Host);
                }
                else
                {
                    return new Uri(v);
                }
            }
             
        }


        Uri GetUriPath(DateTime dateTime)
        {
            string s = $"/post/popular_by_day?day={dateTime.Day}&month={dateTime.Month}&year={dateTime.Year}";

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
            DateTime dateTime = await m_nextPagesFunc().ConfigureAwait(false);

            Uri uri = GetUriPath(dateTime);

            string html = await m_request.GetStringAsync(uri).ConfigureAwait(false);

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

        DateTime m_dateTime = InputData.DateTime;

        public MainPage()
        {
            InitializeComponent();

            InitPermissions();
        }

        void Init()
        {
            DeviceDisplay.KeepScreenOn = true;

            InitSelectView();

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

        DateTime GetNextDateTime()
        {
            DateTime dateTime = m_dateTime;

            m_pagesText.Text = dateTime.ToString();

            InputData.DateTime = dateTime;

            m_dateTime = dateTime.Add(new TimeSpan(-1, 0, 0, 0));

            return dateTime;
        }

       

        void InitSelectView()
        {
            var vs = Http.GetSource();

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


        async void Start(string host)
        {

            if (CreateInput() == false) 
            {
                Task t = DisplayAlert("错误", "Input Error", "确定");

                return;
            }

            var http = new Http(
                host,
                () => MainThread.InvokeOnMainThreadAsync(GetNextDateTime),
                new TimeSpan(0, 0, InputData.TimeOut),
                InputData.MaxSize, InputData.ImgCount);

            var imgs = CreateColl.Create(http, URI_LOAD_COUNT, InputData.ImgCount);


            int timeSpan = InputData.TimeSpan;
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
                    catch(Exception e)
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

            m_dateTime = m_datetime_value.Date;

            InputData.DateTime = m_dateTime;

            string host = m_select_value.SelectedItem.ToString();

            InputData.Host = host;

            Directory.CreateDirectory(ROOT_PATH);

            Start(host);
        }

        void OnScrolled(object sender, ItemsViewScrolledEventArgs e)
        {
            if ((e.LastVisibleItemIndex + 1) != m_source.Count)
            {
                m_awa.SetAwait();
            }
            else
            {
                m_awa.SetAdd();
            }
        }
    }
}