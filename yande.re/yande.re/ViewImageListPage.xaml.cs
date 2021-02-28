using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.ObjectModel;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using Xamarin.Essentials;
using System.IO;

namespace yande.re
{
    public sealed class ViewImageListPageInfo
    {
        public ViewImageListPageInfo(Func<Label, ObservableCollection<Data>, PreLoad> start, string rootPath)
        {
            Start = start;
            RootPath = rootPath;
        }

        public Func<Label, ObservableCollection<Data>, PreLoad> Start { get; }


        public string RootPath { get; }
    }

    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ViewImageListPage : ContentPage
    {
        ViewImageListPageInfo m_info;

        ObservableCollection<Data> m_coll;

        PreLoad m_preLoad;

        public ViewImageListPage(ViewImageListPageInfo info)
        {
            InitializeComponent();

            m_info = info;

            var coll = new ObservableCollection<Data>();

            m_coll = coll;

            m_view.ItemsSource = coll;

            m_preLoad = m_info.Start(m_pagesText, coll);
        }

        Task SaveImage(byte[] buffer)
        {
            return Task.Run(async () =>
            {
                string name = Path.Combine(m_info.RootPath, Path.GetRandomFileName() + ".png");

                using (var file = new FileStream(name, FileMode.Create, FileAccess.Write, FileShare.None, 1, true))
                {

                    await file.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                }
            });

           
        }



        void OnScrolled(object sender, ItemsViewScrolledEventArgs e)
        {
            long n = (long)e.VerticalDelta;

            if (n != 0)
            {
                if (n < 0)
                {
                    m_preLoad.SetWait();
                }
                else if (n > 0 && e.LastVisibleItemIndex + 1 == m_coll.Count)
                {
                    m_preLoad.SetNotWait();
                }
            }
        }

        void OnCollectionViewSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (m_view.SelectedItem != null)
            {

                Task t = SaveImage(((Data)m_view.SelectedItem).Buffer);

                m_view.SelectedItem = null;
            }
        }

        protected override bool OnBackButtonPressed()
        {
            m_preLoad.Cencel();

            return base.OnBackButtonPressed();
        }
    }
}