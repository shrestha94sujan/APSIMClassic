﻿using System;
using System.Collections.Generic;
using System.Text;
using CSGeneral;
using ApsimFile;
using System.IO;


public class RunApsimJob : RunExternalJob
   {
   protected string _SimFileName = "";
   protected string _SumFileName = "";
   protected StreamWriter _SumFile = null;
   public bool _DeleteSim = true;


   public RunApsimJob(String Name, JobRunner JobRunner) 
      : base(Name, JobRunner)
      {
      }
   public string SimFileName
      {
      get
         {
         return _SimFileName;
         }
      set
         {
         _SimFileName = value;
         if (Path.GetDirectoryName(_SimFileName) == "")
            _SimFileName = Path.Combine(Directory.GetCurrentDirectory(), _SimFileName);
         _SumFileName = Path.Combine(Path.GetDirectoryName(_SimFileName),
                        Path.GetFileNameWithoutExtension(_SimFileName) + ".sum");
         _SumFile = new StreamWriter(_SumFileName);
         if (Configuration.getArchitecture() == Configuration.architecture.unix) {
             _Executable = "mono";
             _Arguments =  StringManip.DQuote(Path.Combine("%apsim%", "Model", "Apsim.exe")) + " " + StringManip.DQuote(_SimFileName);
         } else {
             _Executable = Path.Combine("%apsim%", "Model", "Apsim.exe");
             _Arguments = StringManip.DQuote(_SimFileName);
         }    
         }
      }
   public string SumFileName
      {
      get
         {
         return _SumFileName;
         }
      }
   public override void Stop()
      {
      base.Stop();
      lock (this)
         {
         // Permanently stop the job.
         if (_DeleteSim && File.Exists(_SimFileName))
            File.Delete(_SimFileName);
         }
      }
   protected override void OnStdOut(object sender, CSGeneral.DataReceivedEventArgs e)
      {
      lock (this)
         {
         // A handler for an APSIM process writting to stdout.
         if (!_SumFile.BaseStream.CanWrite)
            {
            // This is bad. It means that the associated apsim.exe has been closed but
            // we're still being passed stdout stuff.

            _SumFile = new StreamWriter(_SumFileName, true);
            _SumFile.Write(e.Text);
            _SumFile.Close();
            }
         else if (e.Text != "")
            _SumFile.Write(e.Text);
         if (e.Text.Contains("APSIM  Fatal  Error"))
            _HasErrors = true;
         else if (e.Text.Contains("APSIM Warning Error"))
            _HasWarnings = true;
         }
      }

   protected override void OnStdError(object sender, CSGeneral.DataReceivedEventArgs e)
      {
      lock (this)
         {
         // A handler for an APSIM process writting to stderr.
         // Look for a percent complete
         if (e.Text.Length > 0)
            {
            if (e.Text[0] == '%')
				{
               _PercentComplete = Convert.ToInt32(e.Text.Substring(1));
				int posEol = e.Text.IndexOf('\n');
				if (posEol > 0) 
   					_StdErr += e.Text.Substring(posEol); // Append trailing message, if any
				}
            else
               _StdErr += e.Text;
            }
         }
      }
   protected override void OnExited(object sender)
      {
      // APSIM has finished running, so we need to close the summary file
      // and attempt to run the next simulation
      lock (this)
         {
         if (_StdErr.Length > 0) {
            _SumFile.Write(_StdErr);
         }
         _SumFile.Close();
         _SumFile = null;
         _PercentComplete = 100;
         _IsRunning = false;
         }
      }

   }
   
