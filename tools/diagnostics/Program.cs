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
[assembly: AssemblyDescription("Локальная фоновая диагностика Глюкобоя")]
[assembly: AssemblyVersion("1.1.0.0")]

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

            bool background = args.Length == 1 && args[0] == "--background";
            // Deterministic UI previews: no mutex, report directory, tray, or Collector.
            if (args.Length == 2 && (args[0] == "--render" || args[0] == "--render-background"))
            {
                try
                {
                    string path = Path.GetFullPath(args[1]);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    bool hiddenPreview = args[0] == "--render-background";
                    using (DiagnosticForm form = new DiagnosticForm(true, hiddenPreview, null))
                    {
                        form.ShowInTaskbar = false;
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(-10000, -10000);
                        int shown = 0, activated = 0;
                        form.Shown += delegate { shown++; };
                        form.Activated += delegate { activated++; };
                        if (hiddenPreview)
                        {
                            using (System.Windows.Forms.Timer previewTimer = new System.Windows.Forms.Timer())
                            {
                                previewTimer.Interval = 150;
                                previewTimer.Tick += delegate { previewTimer.Stop(); Application.ExitThread(); };
                                previewTimer.Start();
                                RunForm(form, true);
                            }
                        }
                        else { form.StartCapture(); form.Show(); Application.DoEvents(); }
                        if (hiddenPreview)
                        {
                            if (form.Visible || shown != 0 || activated != 0 || form.CaptureStarted)
                                throw new InvalidOperationException("Фоновая проверка неожиданно показала окно или начала запись.");
                            File.WriteAllText(path + ".state.txt", "initial_visible=false\r\nshown_events=0\r\nactivation_events=0\r\ncollector_started=false\r\n");
                            // The startup assertion above used the real hidden
                            // message loop. Show offscreen only for the bitmap;
                            // preview forms explicitly refuse activation.
                            form.Show();
                        }
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
            EventWaitHandle showRequest = null;
            bool ownsMutex = false;
            try
            {
                string identity = WindowsIdentity.GetCurrent().User.Value;
                showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\Glukoboy.Diagnostics.Show." + identity);
                singleton = new Mutex(true, "Local\\Glukoboy.Diagnostics." + identity, out ownsMutex);
                if (!ownsMutex)
                {
                    if (!background) showRequest.Set();
                    return 0;
                }
                using (DiagnosticForm form = new DiagnosticForm(false, background, showRequest))
                {
                    RunForm(form, background);
                }
                return 0;
            }
            catch (Exception error)
            {
                if (!background) MessageBox.Show("Не удалось открыть сборщик диагностики.\r\n\r\n" + error.Message,
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
                if (showRequest != null) showRequest.Dispose();
            }
        }

        internal static void RunForm(DiagnosticForm form, bool background)
        {
            // Application.Run(context) automatically shows a non-null MainForm.
            // Keep MainForm null and explicitly end the loop on this form's close.
            using (ApplicationContext context = new ApplicationContext())
            {
                form.FormClosed += delegate { context.ExitThread(); };
                form.StartCapture();
                if (!background) form.Show();
                Application.Run(context);
            }
        }
    }

    internal sealed class DiagnosticForm : Form
    {
        private readonly bool renderOnly;
        private readonly bool background;
        private readonly EventWaitHandle showRequest;
        private readonly string automaticRoot;
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
        private volatile string reportDirectory;
        private bool saving;
        private bool exportStarted;
        private bool allowClose;
        private bool balloonShown;

        internal DiagnosticForm(bool preview) : this(preview, false, null) { }

        internal DiagnosticForm(bool preview, bool automatic, EventWaitHandle openSignal)
        {
            renderOnly = preview;
            background = automatic;
            showRequest = openSignal;
            automaticRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlukoboyDiagnostics", "AutomaticCapture");
            ShowInTaskbar = !background;
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
            statusDetail = MakeLabel(background ? "Диагностика запущена вместе с игрой и продолжает работать в фоне." : "Окно можно свернуть. Сборщик работает отдельно от игры.", 9F, FontStyle.Regular);
            statusDetail.Location = new Point(13, 34);
            statusDetail.Size = new Size(650, 25);
            statusDetail.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            statusPanel.Controls.Add(statusTitle);
            statusPanel.Controls.Add(statusDetail);
            layout.Controls.Add(statusPanel, 0, 1);

            Label instructions = MakeLabel(background ?
                "1. Диагностика уже работает. Пользуйтесь компьютером как обычно.\r\n2. После проблемы подождите ещё 10–20 секунд.\r\n3. Нажмите «Сохранить отчёт и выйти» и пришлите полученный ZIP в чат." :
                "1. Оставьте сборщик работать и пользуйтесь компьютером как обычно.\r\n2. Дождитесь ночного режима или момента, когда ребёнок его обойдёт.\r\n3. Подождите ещё 10–20 секунд и нажмите «Сохранить отчёт и выйти».", 10F, FontStyle.Regular);
            instructions.Dock = DockStyle.Fill;
            instructions.Padding = new Padding(0, 5, 0, 0);
            layout.Controls.Add(instructions, 0, 2);

            Label privacy = MakeLabel("Что попадёт в отчёт\r\nСостояния процессов и окон, название активной программы, ошибки Глюкобоя и его расписание. Нажатия клавиш, снимки экрана и звук не записываются. Ничего не отправляется автоматически.", 9F, FontStyle.Regular);
            privacy.Dock = DockStyle.Fill;
            privacy.ForeColor = Color.FromArgb(67, 83, 101);
            layout.Controls.Add(privacy, 0, 3);

            Label trayHint = MakeLabel(background ?
                "Крестик прячет окно в значок. Запись продолжается до сохранения отчёта.\r\nХранятся до 8 последних записей по 6 часов / 20 МиБ; старые удаляются автоматически." :
                "Крестик и кнопка сворачивания прячут окно в значок рядом с часами.\r\nЗапись идёт до сохранения отчёта, но не дольше 6 часов.", 8.5F, FontStyle.Regular);
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
            uiTimer.Tick += delegate {
                if (showRequest != null && showRequest.WaitOne(0)) RestoreWindow();
                RefreshStatus();
            };
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

        internal bool CaptureStarted { get { return worker != null; } }

        protected override bool ShowWithoutActivation { get { return renderOnly || base.ShowWithoutActivation; } }

        internal void StartCapture()
        {
            // Creating the handle does not display or activate the form. The
            // timer and tray menu can later restore this same hidden window.
            IntPtr unusedHandle = Handle;
            if (worker != null) return;
            if (renderOnly)
            {
                statusTitle.Text = "Запись идёт · 00:00:12";
                statusDetail.Text = "Состояние Глюкобоя проверяется каждые полсекунды.";
                return;
            }
            reportDirectory = background ? automaticRoot : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
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
            BackgroundCaptureStore automatic = null;
            try
            {
                if (background)
                {
                    automatic = new BackgroundCaptureStore(automaticRoot);
                    reportDirectory = automatic.CreateSession();
                }
                current = new Collector(reportDirectory);
                collector = current;
                current.Note("collector", background ? "Automatic background collector started; 500 ms interval; 6 hour / 20 MiB sessions, up to 8 retained. Target exit does not stop recording." : "Standalone collector started; interval 500 ms, maximum duration 6 hours.");
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
                    if (BackgroundCaptureStore.ShouldRotate(duration.Elapsed, current.IsRecordingLimitReached))
                    {
                        if (background)
                        {
                            current.Note("rotation", "Closing bounded session; automatic recording continues in the next session.");
                            current.Dispose();
                            if (current.IsOutputFailed) throw new IOException(current.LastError);
                            automatic.MarkComplete(reportDirectory);
                            current = null;
                            collector = null;
                            if (stopEvent.WaitOne(0)) break;
                            reportDirectory = automatic.CreateSession();
                            if (stopEvent.WaitOne(0)) break;
                            current = new Collector(reportDirectory);
                            collector = current;
                            current.Note("rotation", "New automatic background session after duration or size rotation.");
                            duration.Restart();
                            continue;
                        }
                        recordingLimitReached = true;
                        current.Note("limit", "Recording stopped because its size or duration limit was reached.");
                        break;
                    }
                    if (stopEvent.WaitOne(500)) break;
                }
                if (!collectorOutputFailed && current != null) current.Note("collector", "Recording loop stopped.");
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
                    try
                    {
                        current.Dispose();
                        if (automatic != null && !current.IsOutputFailed && String.IsNullOrEmpty(workerError)) automatic.MarkComplete(reportDirectory);
                    }
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
            statusTitle.Text = (background ? "Фоновая запись идёт · " : "Запись идёт · ") + ((int)elapsed.Elapsed.TotalHours).ToString("00") + elapsed.Elapsed.ToString(@"\:mm\:ss");
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
                try { exportPath = CreatePackageInAvailableFolder(background ? automaticRoot : reportDirectory, partial, background); }
                catch (Exception error) { exportError = error.Message; }
                finally { exportFinished = true; }
            });
            exporter.Name = "Glukoboy diagnostics ZIP exporter";
            exporter.IsBackground = true;
            exporter.Start();
        }

        private static string CreatePackageInAvailableFolder(string directory, bool partial, bool automatic)
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
                try { return automatic ? ReportPackage.CreateBackground(directory, destination, partial) : ReportPackage.Create(directory, destination, partial); }
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
            if (!background && !balloonShown)
            {
                balloonShown = true;
                trayIcon.ShowBalloonTip(5000, "Диагностика Глюкобоя", "Окно скрыто. Сборщик остаётся работать. Дважды нажмите значок, чтобы сохранить отчёт.", ToolTipIcon.Info);
            }
        }

        private void RestoreWindow()
        {
            ShowInTaskbar = true;
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
