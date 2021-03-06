﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO.Pipes;
using WeifenLuo.WinFormsUI.Docking;
using DragonNest.ResourceInspector.Dnt.Viewer;
using DragonNest.ResourceInspector.Pak.Viewer;
using System.ServiceModel;
namespace DragonNest.ResourceInspector.Core
{
    using Timer = System.Timers.Timer;
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
    public partial class Main : Form, DNRIService
    {
        const string PipeServiceName = "DNRIS";
        const string PipeName = "net.pipe://localhost";
        const string PipeService = PipeName + @"/" + PipeServiceName; 

        ServiceHost @this;

        public Main()
        {
            InitializeComponent();

            //Do event handlers here to reduce code clutter later. 
            exitToolStripMenuItem.Click += (s, e) => Close();
            dragonNestTableToolStripMenuItem.Click += (s, e) => OpenDnt();
            pakFileToolStripMenuItem1.Click += (s,e) => OpenPakAmalgation();
            pakFileToolStripMenuItem.Click +=  (s,e) => OpenPakAmalgation();
            dragonNestTableFileToolStripMenuItem .Click += (s, e) => OpenDnt();
        }

        public Main(String [] args) : this()
        {
            try
            {
                using (ChannelFactory<DNRIService> serviceFactory = new ChannelFactory<DNRIService>(new NetNamedPipeBinding(), new EndpointAddress(PipeService)))
                {
                    var channel = serviceFactory.CreateChannel();
                    if(channel.IsOnline())
                    {
                        foreach (var argument in args)
                        {
                            var argTrim = argument.Trim() ;
                            if (argTrim.EndsWith(".dnt"))
                                channel.OpenDnt(argument);
                            else if (argTrim.EndsWith(".pak"))
                                channel.OpenPak(argument);
                        }
                        channel.Activate();
                        Close();
                    }
                }
            }
            catch
            {
                @this = new ServiceHost(this, new Uri(PipeName));
                @this.AddServiceEndpoint(typeof(DNRIService), new NetNamedPipeBinding(), PipeService);
                @this.BeginOpen((IAsyncResult ar) => @this.EndOpen(ar), null);

                foreach (var argument in args)
                {
                    var argTrim = argument.Trim();
                    if (argTrim.EndsWith(".dnt"))
                        OpenDnt(argument);
                    else if (argTrim.EndsWith(".pak"))
                        OpenPak(argument);
                }
            }
        }
        #region Pubilc methods
        public void OpenDnt()
        {
            var ofd = new OpenFileDialog() { Filter = "DNT | *.dnt" };
            if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                OpenDnt(ofd.OpenFile());
        }
        public void OpenPakAmalgation()
        {
            var ofd = new OpenFileDialog() { Filter = "PAK | *.pak" };
            ofd.Multiselect = true;

            if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                OpenPakAmalgation(ofd.FileNames.Select(p => File.Open(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)));
        }

        public void OpenPakAmalgation(IEnumerable<Stream> streams)
        {
            var PakAmaOpenerWorker = new BackgroundWorker();
            PakAmaOpenerWorker.DoWork += PakAmaOpenerWorker_DoWork;
            PakAmaOpenerWorker.RunWorkerAsync(streams);
        }

        void PakAmaOpenerWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            var streams  = e.Argument as IEnumerable<Stream>;

            ToolStripProgressBar bar = new ToolStripProgressBar() { Style = ProgressBarStyle.Continuous, Maximum = 100, Value = 0 };
            ToolStripLabel label = new ToolStripLabel("Loading Package Files");
            ToolStripItem[] items = { label, bar };

            Invoke(new Action(() => statusStrip1.Items.AddRange(items)));

            AmalgamatedPakViewer apv = new AmalgamatedPakViewer();
            apv.StatusChangedEvent += (s, a) =>  Invoke(new Action(()=> bar.Value = apv.Status));
            apv.LoadPaks(streams);

            Invoke(new Action(()=>{
                apv.Show(dockPanel1, DockState.Document);
                statusStrip1.Items.Remove(bar);
                statusStrip1.Items.Remove(label);
                foreach(var stream in streams)
                    stream.Dispose();
            }));
        }
    
        #endregion
        #region Service Implementations
        public void OpenDnt(string path)
        {
            OpenDnt(new FileStream(path, FileMode.Open));
        }

        public void OpenPak(string path)
        {
            OpenPakAmalgation(new String[] { path}.Select( p=> new FileStream(p,FileMode.Open)));
        }

        public bool IsOnline()
        {
            return true;
        }

        #endregion

        #region Prviate Methods
        void OpenDnt(Stream stream)
        {
            var DntOpenWorker = new BackgroundWorker();
            DntOpenWorker.DoWork += DntOpenerWorker_DoWork;
            DntOpenWorker.RunWorkerAsync(stream);
        }

        #region Event Handlers
        private void showLinqToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DntViewer.ShowLinq = !showLinqToolStripMenuItem.Checked;
            showLinqToolStripMenuItem.Checked = DntViewer.ShowLinq;
        }

        private void Main_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (@this != null)
                @this.Close();
        }


        private void DntOpenerWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            var stream = e.Argument as Stream;
            var worker = sender as BackgroundWorker;
            bool IsFileStream = (stream is FileStream) ? true : false;

            ToolStripProgressBar bar = new ToolStripProgressBar() { Style = ProgressBarStyle.Continuous, Maximum = 100, Value = 0 };
            ToolStripLabel label = new ToolStripLabel("Loading : " + ((IsFileStream) ? ((FileStream)stream).Name : "File"));
            ToolStripItem[] items = { label, bar };

            DntViewer viewer = new DntViewer();
            viewer.StatusChanged += (s, a) => Invoke(new Action(() => bar.Value = viewer.Status));
            Invoke(new Action(() => statusStrip1.Items.AddRange(items)));
            viewer.LoadDntStream(stream);
            Invoke(new Action(() =>
            {
                viewer.Show(dockPanel1, DockState.Document);
                statusStrip1.Items.Remove(label);
                statusStrip1.Items.Remove(bar);
                stream.Dispose();
                label.Dispose();
                bar.Dispose();
            }));
        }
        #endregion
        #endregion

        private void clearTempFilesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            foreach (var file in Directory.GetFiles(appData + @"\Dragon Nest Resource Inspector\"))
                if(File.Exists(file))
                    File.Delete(file);
        }
    }
}
