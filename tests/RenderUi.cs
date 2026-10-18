using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ParanormalTrainer
{
    internal static class RenderUi
    {
        private static int checks;
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                string rootPath = args.Length == 0 ? Environment.CurrentDirectory : Path.GetFullPath(args[0]);
                string output = Path.Combine(rootPath, "analysis");
                Directory.CreateDirectory(output);
                TrainerWindow window = new TrainerWindow();
                FrameworkElement content = (FrameworkElement)window.Content;
                Call(window, "RefreshControls");
                IList rows = (IList)Field(window, "_rows");
                IList quicks = (IList)Field(window, "_quickRows");
                Check(rows.Count == 138, "all 138 CT features have editor rows");
                Check(quicks.Count == 7, "all seven quick actions exist");
                Check(((Button)Field(window, "_attachButton")).IsEnabled, "attach action is enabled while detached");
                Check(!((Button)Field(window, "_disableButton")).IsEnabled, "disable-all action is disabled while detached");
                Check(quicks.Cast<object>().All(q => !((Button)PublicField(q, "Toggle")).IsEnabled), "all quick writes are disabled while detached");
                Check(rows.Cast<object>().All(r => !((Button)PublicField(r, "Apply")).IsEnabled && !((Button)PublicField(r, "Freeze")).IsEnabled), "all apply and freeze actions are disabled while detached");
                Check(rows.Cast<object>().Count(r => !((Feature)PublicField(r, "Feature")).Writable) == 10, "ten informational entries remain read-only");
                Check(rows.Cast<object>().Where(r => !((Feature)PublicField(r, "Feature")).Writable).All(r => ((Button)PublicField(r, "Apply")).Visibility == Visibility.Collapsed), "read-only entries have no visible write action");

                Call(window, "UpdateCompactLayout", 780.0);
                Render(content, 1040, 740, Path.Combine(output, "ui-quick.png"));
                AssertContained(content, (FrameworkElement)Field(window, "_attachButton"), "normal-size connection button is inside the content");

                Call(window, "ShowPage", true);
                Render(content, 1040, 740, Path.Combine(output, "ui-editors.png"));
                Check(rows.Cast<object>().All(r => ((FrameworkElement)PublicField(r, "Root")).ActualHeight > 0), "all editor rows have measured layouts");
                Check(rows.Cast<object>().Where(r => ((Feature)PublicField(r, "Feature")).Writable).All(r => ((FrameworkElement)PublicField(r, "Input")).ActualWidth >= 100), "writable editors preserve usable input widths");

                TextBox search = (TextBox)Field(window, "_search");
                search.Text = "Current Health";
                Check(VisibleRows(rows).Count == 1 && ((Feature)PublicField(VisibleRows(rows)[0], "Feature")).Id == 108766, "original-name search finds current health");
                search.Text = "";
                Feature turkishFeature = rows.Cast<object>().Select(r => (Feature)PublicField(r, "Feature")).First(f => !String.IsNullOrEmpty(f.TurkishName));
                search.Text = turkishFeature.TurkishName;
                Check(VisibleRows(rows).Any(r => ((Feature)PublicField(r, "Feature")).Id == turkishFeature.Id), "Turkish-name search finds the matching feature");
                search.Text = "zz-no-such-feature-4821";
                Check(VisibleRows(rows).Count == 0, "unmatched search returns no rows");
                search.Text = "";
                string category = rows.Cast<object>().Select(r => ((Feature)PublicField(r, "Feature")).Category).First(c => !String.IsNullOrEmpty(c));
                typeof(TrainerWindow).GetField("_category", PrivateInstance).SetValue(window, category);
                Call(window, "ApplyFilter");
                Check(VisibleRows(rows).Count > 0 && VisibleRows(rows).All(r => ((Feature)PublicField(r, "Feature")).Category == category), "category filter includes only the selected category");
                typeof(TrainerWindow).GetField("_category", PrivateInstance).SetValue(window, "Tüm kategoriler");
                Call(window, "ApplyFilter");
                Check(VisibleRows(rows).Count == 138, "clearing filters restores every editor");

                Call(window, "UpdateCompactLayout", 620.0);
                Render(content, 860, 580, Path.Combine(output, "ui-minimum.png"));
                Check(((FrameworkElement)Field(window, "_houseArt")).Visibility == Visibility.Collapsed, "compact layout releases sidebar space");
                AssertContained(content, (FrameworkElement)Field(window, "_attachButton"), "minimum-size connection button is inside the content");
                AssertContained(content, (FrameworkElement)Field(window, "_hotkeyButton"), "minimum-size hotkey button is inside the content");
                FrameworkElement firstRow = (FrameworkElement)PublicField(rows[0], "Root");
                FrameworkElement firstFreeze = (FrameworkElement)PublicField(rows[0], "Freeze");
                AssertContained(firstRow, firstFreeze, "minimum-size editor freeze control stays within its row");
                Check(!((DispatcherTimer)Field(window, "_timer")).IsEnabled, "rendering never starts the polling timer");
                Check((IntPtr)Field(window, "_windowHandle") == IntPtr.Zero, "rendering never creates a native window or registers hotkeys");
                Check(!((TrainerEngine)Field(window, "_engine")).IsAttached, "rendering never attaches to a game process");
                Console.WriteLine("PASS: " + checks + " UI layout checks. No Window.Show, desktop input, timer, hotkey registration or process attachment.");
                File.WriteAllText(Path.Combine(output,"ui-test-result.txt"),"Passed: "+checks+"\r\nScope: WPF layout, rendering, search, filtering and disabled controls\r\nUTC: "+DateTime.UtcNow.ToString("o"));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static void Render(FrameworkElement visual, int width, int height, string path)
        {
            visual.Measure(new Size(width, height));
            visual.Arrange(new Rect(0, 0, width, height));
            visual.UpdateLayout();
            RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(path)) encoder.Save(stream);
            Console.WriteLine("Rendered " + width + "x" + height + ": " + path);
        }

        private static void AssertContained(FrameworkElement parent, FrameworkElement child, string label)
        {
            Rect bounds = child.TransformToAncestor(parent).TransformBounds(new Rect(new Point(0, 0), child.RenderSize));
            Check(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= parent.ActualWidth + 1 && bounds.Bottom <= parent.ActualHeight + 1, label + " [" + bounds + "]");
        }

        private static List<object> VisibleRows(IList rows)
        {
            return rows.Cast<object>().Where(r => ((FrameworkElement)PublicField(r, "Root")).Visibility == Visibility.Visible).ToList();
        }

        private static object Field(object target, string name)
        {
            return target.GetType().GetField(name, PrivateInstance).GetValue(target);
        }

        private static object PublicField(object target, string name)
        {
            return target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance).GetValue(target);
        }

        private static object Call(object target, string name, params object[] args)
        {
            return target.GetType().GetMethod(name, PrivateInstance).Invoke(target, args);
        }

        private static void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("FAILED: " + label);
            checks++;
            Console.WriteLine("PASS " + checks + ": " + label);
        }
    }
}
