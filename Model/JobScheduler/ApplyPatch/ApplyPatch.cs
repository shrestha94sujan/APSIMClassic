﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Diagnostics;
using System.IO;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using CSGeneral;

class JobSchedulerApplyPatch
{
    struct PatchDetail
    {
        public string FileName;
        public string Status;
        public string Revision;
    }

    /// <summary>
    /// This program applies a patch to an APSIM directory. It also copies all patched files to 
    /// a temporary directory so that they can be later compared with what Bob runs.
    /// </summary>
    static int Main(string[] args)
    {
        try
        {
            Dictionary<string,string> Macros = Utility.ParseCommandLine(args);
            Go(Macros);
        }
        catch (Exception err)
        {
            Console.WriteLine(err.Message);
            return 1;
        }
        return 0;
    }

    private static void Go(Dictionary<string,string> Macros)
    {
        if (!Macros.ContainsKey("Directory") || !Macros.ContainsKey("PatchFileName"))
            throw new Exception("Usage: ApplyPatch Directory=DirectoryName PatchFileName=patchfile");

        string PatchFileName = Macros["PatchFileName"];
        string ApsimDirectoryName = Macros["Directory"];
        if (!File.Exists(PatchFileName))
        {
            WebClient Client = new WebClient();
            string localfile = Path.Combine(Path.GetTempPath(), Path.GetFileName(PatchFileName));
            Console.WriteLine("Downloading " + PatchFileName + " to " + localfile);
            Client.DownloadFile(PatchFileName, localfile);
            PatchFileName = localfile;
        }

        // Unzip the files.
        Zip.UnZipFiles(PatchFileName, ApsimDirectoryName, "");


        // Read in the patch information.
        List<PatchDetail> FileDetails = new List<PatchDetail>();
        string RevisionsFileName = Path.Combine(ApsimDirectoryName, "patch.revisions");
        if (!File.Exists(RevisionsFileName))
            throw new Exception("Cannot find the patch.revisions file in the APSIM root directory");
        StreamReader In = new StreamReader(RevisionsFileName);
        while (!In.EndOfStream)
        {
            string Line = In.ReadLine();
            StringCollection LineBits = StringManip.SplitStringHonouringQuotes(Line, " ");
            if (LineBits.Count == 3)
            {
                // Convert either '/' or '\' to the local directory separator
                LineBits[0] = LineBits[0].Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar); 
                FileDetails.Add(new PatchDetail {FileName=LineBits[0], Status=LineBits[1], Revision=LineBits[2] });
            }
        }
        In.Close();
        File.Delete(RevisionsFileName);

        // Find SVN.exe on the path.
        string SVNFileName;
        if (Path.DirectorySeparatorChar == '/') SVNFileName = Utility.FindFileOnPath("svn");
        else SVNFileName = Utility.FindFileOnPath("svn.exe");
        if (SVNFileName == "")
            throw new Exception("Cannot find svn on PATH");

        // Get a list of files currently known to SVN and their tip revision numbers.
        Process SVN = Utility.RunProcess(SVNFileName, "-v stat", ApsimDirectoryName);
        string SVNStdOut = Utility.CheckProcessExitedProperly(SVN);
        string[] Lines = SVNStdOut.Split("\r\n".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
        Dictionary<string, string> SVNFileNames = new Dictionary<string, string>();
        Regex R = new Regex("\\S+");
        foreach (string Line in Lines)
        {
            if (Line.Length >= 9 && Line[0] != '?' && Line.Substring(0, 9).Trim() != "")
            {
                // Get the tip revision and the filename from the SVN line.
                MatchCollection Matches = R.Matches(Line.Substring(10));
                if (Matches.Count > 3)
                {
                    string TipRevision = Matches[1].Value;
                    string FileName = "";
                    for (int i = 3; i < Matches.Count; i++)
                    {
                        FileName += Matches[i].Value + " ";
                    }
                    FileName = FileName.Trim();
                    SVNFileNames.Add(FileName, TipRevision);
                }
            }
        }

        // Some of the files in the patch file will be additions or deletions. We need to tell SVN 
        // about these. We also need to check that all files are up to date.
        string OutOfDateFileNames = "";
        foreach (PatchDetail FileDetail in FileDetails)
        {
            string FullFileName = Path.Combine(ApsimDirectoryName, FileDetail.FileName);
            if (Path.GetFileName(FullFileName) != "patch.revisions")
            {
                string Arguments = null;
                if (FileDetail.Status == "Deleted")
                    Arguments += "delete --force " + StringManip.DQuote(FullFileName);
                else if (FileDetail.Status == "Added")
                    Arguments += "add --force " + StringManip.DQuote(FullFileName);

                if (Arguments != null)
                {
                    EnsureDirectoryIsUnderSVN(Path.GetDirectoryName(FullFileName), SVNFileName);
                    Console.WriteLine(SVNFileName + " " + Arguments);
                    Process P = Utility.RunProcess(SVNFileName, Arguments, ApsimDirectoryName);
                    Console.WriteLine(Utility.CheckProcessExitedProperly(P));
                }
                else
                {
                    if (!SVNFileNames.ContainsKey(FileDetail.FileName))
                        throw new Exception("Cannot find SVN info for patched file: " + FileDetail.FileName);
                    string TipRevision = SVNFileNames[FileDetail.FileName];

                    if (TipRevision != "?" && TipRevision != FileDetail.Revision)
                        OutOfDateFileNames += "\r\n" + FileDetail.FileName + " Tip: " + TipRevision + " File: " + FileDetail.Revision;
                }
            }
        }

        if (OutOfDateFileNames != "")
            throw new Exception("These files are out of date: " + OutOfDateFileNames);
    }

    /// <summary>
    /// Ensure a directory is under SVN control.
    /// </summary>
    private static void EnsureDirectoryIsUnderSVN(string DirectoryName, string SVNFileName)
    {
        if (!Directory.Exists(Path.Combine(DirectoryName, ".svn")))
        {
            int PosSlash = DirectoryName.LastIndexOf(Path.DirectorySeparatorChar);
            if (PosSlash == -1)
                throw new Exception("Invalid directory found: " + DirectoryName);
            string ParentName = DirectoryName.Substring(0, PosSlash);
            string ChildName = DirectoryName.Substring(PosSlash + 1);
            if (!Directory.Exists(Path.Combine(ParentName, ".svn")))
                EnsureDirectoryIsUnderSVN(ParentName, SVNFileName);  // parent dir.
            Directory.CreateDirectory(DirectoryName);
            Console.WriteLine(SVNFileName + " add " + StringManip.DQuote(DirectoryName));
            Process P = Utility.RunProcess(SVNFileName, "add " + StringManip.DQuote(DirectoryName), DirectoryName);
            Console.WriteLine(Utility.CheckProcessExitedProperly(P));
        }
    }

}
