using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal sealed class DiagnosticFixture : Form
{
    private readonly string control;
    private readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer();
    private string previous = "";
    internal DiagnosticFixture(string path)
    {
        control = path;
        Text = "Glukoboy diagnostics TEST WINDOW";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(-20000, -20000, 200, 100);
        poll.Interval = 30;
        poll.Tick += ReadCommand;
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get { CreateParams value = base.CreateParams; value.ExStyle |= 0x08000000 | 0x80; return value; }
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Bounds = new Rectangle(-20000, -20000, 200, 100);
        poll.Start();
        File.WriteAllText(control + ".ready", "ready");
    }
    private void ReadCommand(object sender, EventArgs e)
    {
        string command;
        try { command = File.Exists(control) ? File.ReadAllText(control).Trim() : ""; }
        catch (IOException) { return; }
        if (command == previous || command.Length == 0) return;
        previous = command;
        if (command == "hide") Hide();
        else if (command == "show") { ShowWindow(Handle, 4); Bounds = new Rectangle(-20000, -20000, 200, 100); }
        else if (command == "minimize") ShowWindow(Handle, 7);
        File.WriteAllText(control + ".ack", command);
        if (command == "hang") Thread.Sleep(1600);
        if (command == "quit") { poll.Stop(); Close(); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) poll.Dispose();
        base.Dispose(disposing);
    }
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [STAThread] private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new DiagnosticFixture(args[0]));
    }
}
