using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Диагностика Глюкобоя")]
[assembly: AssemblyProduct("Glukoboy Diagnostics")]
[assembly: AssemblyDescription("Отдельный переносной сборщик диагностики Глюкобоя")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace GlukoboyDiagnostics
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static int Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // A deterministic UI preview: no mutex, report directory, or Collector.
            if (args.Length == 2 && args[0] == "--render")
            {
                try
                {
                    string path = Path.GetFullPath(args[1]);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (DiagnosticForm form = new DiagnosticForm(true))
                    {
                        form.ShowInTaskbar = false;
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(-10000, -10000);
                        form.Show();
                        form.Refresh();
                        using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    return 0;
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine(error.Message);
                    return 1;
                }
            }

            Mutex singleton = null;
            bool ownsMutex = false;
            try
            {
                string identity = WindowsIdentity.GetCurrent().User.Value;
                singleton = new Mutex(true, "Local\\Glukoboy.Diagnostics." + identity, out ownsMutex);
                if (!ownsMutex)
                {
                    MessageBox.Show("Сборщик диагностики уже запущен. Найдите его значок рядом с часами Windows (возможно, под стрелкой скрытых значков).",
                        "Диагностика Глюкобоя", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                Application.Run(new DiagnosticForm(false));
                return 0;
            }
            catch (Exception error)
            {
                MessageBox.Show("Не удалось открыть сборщик диагностики.\r\n\r\n" + error.Message,
                    "Диагностика Глюкобоя", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            finally
            {
                if (singleton != null)
                {
                    if (ownsMutex) singleton.ReleaseMutex();
                    singleton.Dispose();
                }
            }
        }
    }

    internal sealed class DiagnosticForm : Form
    {
        private readonly bool renderOnly;
        private readonly Label statusTitle;
        private readonly Label statusDetail;
        private readonly Button saveButton;
        private readonly System.Windows.Forms.Timer uiTimer;
        private readonly NotifyIcon trayIcon;
        private readonly ManualResetEvent stopEvent = new ManualResetEvent(false);
        private readonly Stopwatch elapsed = new Stopwatch();
        private Stopwatch stoppingElapsed;
        private Thread worker;
        private volatile Collector collector;
        private volatile bool workerFinished;
        private volatile bool recordingLimitReached;
        private volatile bool collectorOutputFailed;
        private volatile string workerError;
        private volatile bool exportFinished;
        private volatile string exportPath;
        private volatile string exportError;
        private string lastExportFailure;
        private string reportDirectory;
        private bool saving;
        private bool exportStarted;
        private bool allowClose;
        private bool balloonShown;

        internal DiagnosticForm(bool preview)
        {
            renderOnly = preview;
            Text = "Диагностика Глюкобоя";
            Font = new Font("Segoe UI", 10F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(740, 440);
            MinimumSize = new Size(756, 479);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(247, 249, 252);
            ForeColor = Color.FromArgb(31, 44, 61);
            Icon = SystemIcons.Information;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(24, 18, 24, 18);
            layout.ColumnCount = 1;
            layout.RowCount = 6;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 79));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
            Controls.Add(layout);

            Label heading = MakeLabel("Поможем разобраться с ночным режимом", 18F, FontStyle.Bold);
            heading.Dock = DockStyle.Fill;
            layout.Controls.Add(heading, 0, 0);

            Panel statusPanel = new Panel();
            statusPanel.Dock = DockStyle.Fill;
            statusPanel.BackColor = Color.FromArgb(231, 241, 250);
            statusPanel.Margin = new Padding(0, 0, 0, 10);
            statusTitle = MakeLabel("Подготовка к записи…", 12F, FontStyle.Bold);
            statusTitle.Location = new Point(13, 8);
            statusTitle.Size = new Size(650, 25);
            statusTitle.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            statusDetail = MakeLabel("Окно можно свернуть. Сборщик работает отдельно от игры.", 9F, FontStyle.Regular);
            statusDetail.Location = new Point(13, 34);
            statusDetail.Size = new Size(650, 25);
            statusDetail.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            statusPanel.Controls.Add(statusTitle);
            statusPanel.Controls.Add(statusDetail);
            layout.Controls.Add(statusPanel, 0, 1);

            Label instructions = MakeLabel("1. Оставьте сборщик работать и пользуйтесь компьютером как обычно.\r\n2. Дождитесь ночного режима или момента, когда ребёнок его обойдёт.\r\n3. Подождите ещё 10–20 секунд и нажмите «Сохранить отчёт и выйти».", 10F, FontStyle.Regular);
            instructions.Dock = DockStyle.Fill;
            instructions.Padding = new Padding(0, 5, 0, 0);
            layout.Controls.Add(instructions, 0, 2);

            Label privacy = MakeLabel("Что попадёт в отчёт\r\nСостояния процессов и окон, название активной программы, ошибки Глюкобоя и его расписание. Нажатия клавиш, снимки экрана и звук не записываются. Ничего не отправляется автоматически.", 9F, FontStyle.Regular);
            privacy.Dock = DockStyle.Fill;
            privacy.ForeColor = Color.FromArgb(67, 83, 101);
            layout.Controls.Add(privacy, 0, 3);

            Label trayHint = MakeLabel("Крестик и кнопка сворачивания прячут окно в значок рядом с часами.\r\nЗапись идёт до сохранения отчёта, но не дольше 6 часов.", 8.5F, FontStyle.Regular);
            trayHint.Dock = DockStyle.Fill;
            trayHint.ForeColor = Color.FromArgb(86, 101, 119);
            layout.Controls.Add(trayHint, 0, 4);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            buttons.Margin = Padding.Empty;
            buttons.Padding = new Padding(0, 8, 0, 0);
            saveButton = new Button();
            saveButton.Text = "Сохранить отчёт и выйти";
            saveButton.Size = new Size(242, 36);
            saveButton.BackColor = Color.FromArgb(35, 103, 177);
            saveButton.ForeColor = Color.White;
            saveButton.FlatStyle = FlatStyle.Flat;
            saveButton.FlatAppearance.BorderSize = 0;
            saveButton.Margin = new Padding(10, 0, 0, 0);
            saveButton.Click += delegate { RequestSave(); };
            buttons.Controls.Add(saveButton);
            Button hideButton = new Button();
            hideButton.Text = "Свернуть в значок";
            hideButton.Size = new Size(184, 36);
            hideButton.FlatStyle = FlatStyle.Flat;
            hideButton.FlatAppearance.BorderColor = Color.FromArgb(186, 197, 211);
            hideButton.Margin = Padding.Empty;
            hideButton.Click += delegate { HideToTray(); };
            buttons.Controls.Add(hideButton);
            layout.Controls.Add(buttons, 0, 5);

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Открыть окно", null, delegate { RestoreWindow(); });
            menu.Items.Add("Сохранить отчёт и выйти", null, delegate { RestoreWindow(); RequestSave(); });
            trayIcon = new NotifyIcon();
            trayIcon.Icon = SystemIcons.Information;
            trayIcon.Text = "Диагностика Глюкобоя";
            trayIcon.ContextMenuStrip = menu;
            trayIcon.DoubleClick += delegate { RestoreWindow(); };
            trayIcon.Visible = !renderOnly;

            uiTimer = new System.Windows.Forms.Timer();
            uiTimer.Interval = 500;
            uiTimer.Tick += delegate { RefreshStatus(); };
            Resize += delegate { if (WindowState == FormWindowState.Minimized && !renderOnly) HideToTray(); };
            if (renderOnly)
            {
                statusTitle.Text = "Запись идёт · 00:00:12";
                statusDetail.Text = "Состояние Глюкобоя проверяется каждые полсекунды.";
            }
        }

        private static Label MakeLabel(string text, float size, FontStyle style)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = new Font("Segoe UI", size, style);
            label.AutoSize = false;
            label.Margin = Padding.Empty;
            return label;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (renderOnly)
            {
                statusTitle.Text = "Запись идёт · 00:00:12";
                statusDetail.Text = "Состояние Глюкобоя проверяется каждые полсекунды.";
                return;
            }
            reportDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GlukoboyDiagnostics", "Reports", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            elapsed.Start();
            worker = new Thread(Collect);
            worker.Name = "Glukoboy diagnostics collector";
            worker.IsBackground = true;
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            uiTimer.Start();
        }

        private void Collect()
        {
            Collector current = null;
            try
            {
                current = new Collector(reportDirectory);
                collector = current;
                current.Note("collector", "Standalone collector started; interval 500 ms, maximum duration 6 hours.");
                Stopwatch duration = Stopwatch.StartNew();
                while (!stopEvent.WaitOne(0))
                {
                    current.Sample();
                    if (current.IsOutputFailed)
                    {
                        collectorOutputFailed = true;
                        workerError = "Не удалось записать часть данных. " + current.LastError;
                        break;
                    }
                    if (current.IsRecordingLimitReached || duration.Elapsed >= TimeSpan.FromHours(6))
                    {
                        recordingLimitReached = true;
                        current.Note("limit", "Recording stopped because its size or duration limit was reached.");
                        break;
                    }
                    if (stopEvent.WaitOne(500)) break;
                }
                if (!collectorOutputFailed) current.Note("collector", "Recording loop stopped.");
            }
            catch (Exception error)
            {
                workerError = error.GetType().Name + ": " + error.Message;
                if (current != null)
                {
                    try { current.Note("error", workerError); } catch { }
                }
            }
            finally
            {
                if (current != null)
                {
                    try { current.Dispose(); }
                    catch (Exception error) { workerError = error.GetType().Name + ": " + error.Message; }
                    if (current.IsOutputFailed)
                    {
                        collectorOutputFailed = true;
                        if (String.IsNullOrEmpty(workerError)) workerError = "Не удалось записать часть данных. " + current.LastError;
                    }
                }
                workerFinished = true;
            }
        }

        private void RefreshStatus()
        {
            if (saving)
            {
                if (!exportStarted && (workerFinished || stoppingElapsed.Elapsed >= TimeSpan.FromSeconds(5)))
                    StartExport(!workerFinished || collectorOutputFailed || !String.IsNullOrEmpty(workerError));
                if (exportFinished) FinishExport();
                return;
            }
            if (!String.IsNullOrEmpty(lastExportFailure))
            {
                statusTitle.Text = "Не удалось создать ZIP. Данные сохранены в папке.";
                statusDetail.Text = ShortText(lastExportFailure, 110);
                return;
            }
            if (!String.IsNullOrEmpty(workerError))
            {
                statusTitle.Text = "Запись прервана. Доступные данные можно сохранить.";
                statusDetail.Text = ShortText(workerError, 100);
                return;
            }
            if (workerFinished)
            {
                statusTitle.Text = recordingLimitReached ? "Достигнут предел записи. Сохраните отчёт." : "Запись завершена. Сохраните отчёт.";
                statusDetail.Text = "Собранные данные остаются на этом компьютере.";
                return;
            }
            statusTitle.Text = "Запись идёт · " + elapsed.Elapsed.ToString(@"hh\:mm\:ss");
            Collector current = collector;
            if (current != null)
            {
                string lastError = current.LastError;
                statusDetail.Text = ShortText(String.IsNullOrEmpty(lastError) ? current.Status : "Есть замечания: " + lastError, 110);
            }
            else statusDetail.Text = "Подготовка папки отчёта…";
        }

        private static string ShortText(string value, int maximum)
        {
            if (String.IsNullOrEmpty(value)) return "Состояние Глюкобоя проверяется каждые полсекунды.";
            string oneLine = value.Replace('\r', ' ').Replace('\n', ' ');
            return oneLine.Length <= maximum ? oneLine : oneLine.Substring(0, maximum - 1) + "…";
        }

        private void RequestSave()
        {
            if (renderOnly || saving) return;
            lastExportFailure = null;
            saving = true;
            saveButton.Enabled = false;
            statusTitle.Text = "Завершаю запись…";
            statusDetail.Text = "Можно подождать здесь. Окно продолжает работать.";
            if (stoppingElapsed == null) stoppingElapsed = Stopwatch.StartNew();
            stopEvent.Set();
            RefreshStatus();
        }

        private void StartExport(bool partial)
        {
            exportStarted = true;
            statusTitle.Text = partial ? "Сохраняю доступную часть отчёта…" : "Собираю ZIP с отчётом…";
            statusDetail.Text = partial ? "Не все проверки успели завершиться. Это будет отмечено в ZIP." : "Отчёт появится на рабочем столе или в папке документов.";
            Thread exporter = new Thread(delegate()
            {
                try { exportPath = CreatePackageInAvailableFolder(reportDirectory, partial); }
                catch (Exception error) { exportError = error.Message; }
                finally { exportFinished = true; }
            });
            exporter.Name = "Glukoboy diagnostics ZIP exporter";
            exporter.IsBackground = true;
            exporter.Start();
        }

        private static string CreatePackageInAvailableFolder(string directory, bool partial)
        {
            string[] destinations = new string[] {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.GetDirectoryName(directory)
            };
            Exception lastError = null;
            foreach (string destination in destinations)
            {
                if (String.IsNullOrEmpty(destination)) continue;
                try { return ReportPackage.Create(directory, destination, partial); }
                catch (Exception error) { lastError = error; }
            }
            throw new IOException("Не удалось создать ZIP ни в одной папке. " + (lastError == null ? "" : lastError.Message), lastError);
        }

        private void FinishExport()
        {
            exportFinished = false;
            if (!String.IsNullOrEmpty(exportPath))
            {
                uiTimer.Stop();
                RestoreWindow();
                try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + exportPath + "\"") { UseShellExecute = true }); }
                catch { }
                MessageBox.Show(this, "Отчёт сохранён:\r\n\r\n" + exportPath + "\r\n\r\nПришлите ZIP в чат. Сам сборщик ничего не отправляет.",
                    "Отчёт готов", MessageBoxButtons.OK, MessageBoxIcon.Information);
                allowClose = true;
                Close();
                return;
            }
            saving = false;
            exportStarted = false;
            saveButton.Enabled = true;
            saveButton.Text = "Повторить сохранение";
            statusTitle.Text = "Не удалось создать ZIP. Данные сохранены в папке.";
            statusDetail.Text = ShortText(exportError, 110);
            lastExportFailure = exportError;
            RestoreWindow();
            MessageBox.Show(this, "Не удалось создать ZIP:\r\n" + exportError + "\r\n\r\nСобранные данные не удалены:\r\n" + reportDirectory + "\r\n\r\nОсвободите место или проверьте доступ к папкам и повторите сохранение.",
                "Не удалось сохранить отчёт", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            exportError = null;
        }

        private void HideToTray()
        {
            if (renderOnly) return;
            Hide();
            if (!balloonShown)
            {
                balloonShown = true;
                trayIcon.ShowBalloonTip(5000, "Диагностика Глюкобоя", "Окно скрыто. Сборщик остаётся работать. Дважды нажмите значок, чтобы сохранить отчёт.", ToolTipIcon.Info);
            }
        }

        private void RestoreWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.WindowsShutDown)
            {
                allowClose = true;
                stopEvent.Set();
            }
            if (!allowClose && !renderOnly)
            {
                e.Cancel = true;
                HideToTray();
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                uiTimer.Stop();
                uiTimer.Dispose();
                trayIcon.Visible = false;
                trayIcon.ContextMenuStrip.Dispose();
                trayIcon.Dispose();
                if (worker == null || workerFinished) stopEvent.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
