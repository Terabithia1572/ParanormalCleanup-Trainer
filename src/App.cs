using System;
using System.Windows;

namespace ParanormalTrainer
{
    internal static class App
    {
        [STAThread]
        public static void Main()
        {
            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
            {
                MessageBox.Show(e.Exception.Message, "Paranormal Cleanup Trainer", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };
            app.Run(new TrainerWindow());
        }
    }
}
