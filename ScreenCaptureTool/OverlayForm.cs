using System;
using System.Windows.Forms;

namespace ScreenCaptureTool
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            // 创建主窗体(托盘应用)
            var mainForm = new MainForm();
            
            // 必须将窗体传给 Application.Run()
            Application.Run(mainForm);
        }
    }
}
