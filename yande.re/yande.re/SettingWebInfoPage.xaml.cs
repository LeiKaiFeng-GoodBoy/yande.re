using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace yande.re
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class SettingWebInfoPage : ContentPage
    {
        public SettingWebInfoPage()
        {
            InitializeComponent();


            m_text.Text = WebInfoHelper.CreateInputText();
        }

        void OnInput(object sender, EventArgs e)
        {
            string s = m_text.Text;

            if (WebInfoHelper.TryCreate(s, out string message))
            {
                InputData.WebInfo2 = s;

                DisplayAlert("消息", "输入成功", "确定");
            }
            else
            {
                DisplayAlert("错误", message, "确定");
            }
        }
    }
}