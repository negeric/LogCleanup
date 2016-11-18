using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;
using System.Xml;
using System.Xml.Linq;
using System.IO;
using System.Diagnostics;
using Ionic.Zip;
using System.Runtime.InteropServices;

namespace LogCleanup
{
    // <author>Jeff Anderson https://jeff.forsale</author>
    // <date>October 24, 2016 11:29:58 AM </date>
    // <summary>Log file cleanup utility</summary>
    class Program
    {
        //Setup window properties to allow us to hide the console
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        const int SW_HIDE = 0;
        const int SW_SHOW = 5;
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        //Using CommandLine from https://commandline.codeplex.com/
        class Options
        {
            [Option('n',"name", Required = false, HelpText = "Specify a directory configuration by Name, will only run a path that matches this name")]
            public string strName { get; set; }
            [Option('d', "debug", Required = false, HelpText = "Enable debug output to console window.")]
            public bool bDebug { get; set; }       
            [Option('s',"showDialog", Required = false, HelpText = "Show Console dialog, enabled by default when debug flag is set")]         
            public bool bShowDialog { get; set; }  
            [ParserState]
            public IParserState LastParserState { get; set; }
            [HelpOption]
            public string GetUsage()
            {
                return HelpText.AutoBuild(this, (HelpText current) => HelpText.DefaultParsingErrorsHandler(this, current));
            }
        }
        //Setup static working variables       
        private static string strName; 
        private static bool bDebug;
        private static bool bShowDialog;
        private static bool bRecursive;
        private static int iAccessDenied;
        private static int iArchivedCount;
        private static int iArchivesDeletedCount;
        private static string strIncludeExtension;
        private static List<string> filterExtension;                
        //Setup static working variables
        static void Main(string[] args)
        {            
            //Set access denied counter to 0
            iAccessDenied = 0;
            //Set static working variables based on command line args
            var options = new Options();
            if (Parser.Default.ParseArguments(args, options))
            {                
                bDebug = options.bDebug;
                strName = options.strName;
                //Show dialog if -s --showDialog or -d --debug flags are set
                bShowDialog = (bDebug || options.bShowDialog) ? true : false;
            }
            if (!bShowDialog)
            {
                hideWindow();
                debug("Hiding console window", "info");
            }
            debug("Starting Log Cleanup", "info");        
            parseXml();           
        }
        private static void hideWindow()
        {
            var handle = GetConsoleWindow();
            ShowWindow(handle, SW_HIDE);
        }
        private static void parseXml()
        {
            //Start a new timer.  If debug is enabled, the elapsed time will be displayed
            var timer = Stopwatch.StartNew();
            timer.Start();
            try
            {
                string xmlPath = System.AppDomain.CurrentDomain.BaseDirectory + @"\config\paths.xml";
                XDocument doc = XDocument.Load(xmlPath);
                var p = from element in doc.Elements("paths").Elements("path") select element;
                //IEnumerable<XElement> p = doc.Elements();
                foreach (var t in p)
                {
                    //Check if a name was specified
                    bool bContinue = false;
                    if (!String.IsNullOrEmpty(strName))
                    {
                        if (t.Element("name").Value.ToString().ToLower() != strName.ToLower())
                            bContinue = true;
                        else
                            debug("Matched a path node by name " + strName, "info");
                    }
                    if (bContinue)
                        continue;
                    try
                    {
                        debug("Location: " + t.Element("location").Value, "info");
                        debug("File Extensions: " + t.Element("extensions").Value, "info");
                        debug("Archive Days: " + t.Element("archiveDays").Value, "info");
                        debug("Delete Archive Days: " + t.Element("deleteArchiveDays").Value, "info");
                        //Check if params are valid
                        if (validParams(t.Element("location").Value, t.Element("archiveDays").Value))
                        {
                            //Valid get files older than day
                            string location = t.Element("location").Value;
                            int archiveDays = Convert.ToInt32(t.Element("archiveDays").Value);
                            bool dryRun = (!String.IsNullOrEmpty(t.Element("dryRun").Value) && t.Element("dryRun").Value.ToLower() == "false") ? false : true;
                            bool deleteOriginal = (!String.IsNullOrEmpty(t.Element("deleteOriginal").Value) && t.Element("deleteOriginal").Value.ToLower() == "false") ? false : true;
                            string archiveDirectory = (!String.IsNullOrEmpty(t.Element("archiveDirectory").Value)) ? t.Element("archiveDirectory").Value : null;
                            bRecursive = (!String.IsNullOrEmpty(t.Element("recursive").Value) && t.Element("recursive").Value.ToLower() == "true") ? true : false;
                            if (dryRun)
                                debug("Dry Run! Files/Archives will not be deleted.  Change <dryRun>false</dryRun> to true", "info");
                            strIncludeExtension = t.Element("extensions").Value;
                            //Check filter file extensions
                            bool fileExtensionFilter = false;
                            //Create a blank list to hold the filtered file extensions
                            filterExtension = new List<string>();
                            //Check if 1-exclude flag was set and contains paths
                            if (!String.IsNullOrEmpty(strIncludeExtension))
                            {
                                //Set fileExtensionFilter to true so we don't run code if there are no filters
                                fileExtensionFilter = true;

                                //Check if multiple extensions were specified
                                if (strIncludeExtension.Contains(','))
                                {
                                    //Multiple extensions were provided, expand to array
                                    foreach (string tmpExt in strIncludeExtension.Split(','))
                                    {
                                        //Check if the user added a . before the extension, if not, add it
                                        if (tmpExt.Substring(0, 1) != ".")
                                        {
                                            filterExtension.Add("." + tmpExt);
                                            debug("Added new filter: ." + tmpExt, "info");
                                        }
                                        else
                                        {
                                            filterExtension.Add(tmpExt);
                                            debug("Added new filter: " + tmpExt, "info");
                                        }
                                    }
                                }
                                else //Single file extension provided
                                {
                                    //Check if the user added a . before the extension, if not, add it
                                    if (strIncludeExtension.Substring(0, 1) != ".")
                                    {
                                        filterExtension.Add("." + strIncludeExtension);
                                        debug("Added new filter: ." + strIncludeExtension, "info");
                                    }
                                    else
                                    {
                                        filterExtension.Add(strIncludeExtension);
                                        debug("Added new filter: " + strIncludeExtension, "info");
                                    }
                                }
                            }
                            //Get a list of files with the extension filter
                            IList<string> files = new List<string>();
                            files = GetFiles(location, files, archiveDays, fileExtensionFilter, filterExtension);
                            //Loop through files

                            foreach (string file in files)
                            {
                                DateTime cd = File.GetCreationTime(file);
                                string archive = "Archive_" + cd.ToString("yyyy-MM-dd") + ".zip";
                                addFileToArchive(file, archive, archiveDirectory, deleteOriginal, dryRun);
                                iArchivedCount++;
                            }
                            //Delete Old archives       
                            if (!String.IsNullOrEmpty(t.Element("deleteArchiveDays").Value))
                            {
                                int deleteArchiveDays;
                                bool bDel = Int32.TryParse(t.Element("deleteArchiveDays").Value, out deleteArchiveDays);
                                if (bDel)
                                {
                                    debug("Deleting archives older than " + deleteArchiveDays + " days", "info");
                                    IList<string> archives = new List<string>();
                                    DeleteArchives(location, archives, deleteArchiveDays, dryRun);
                                }
                                else
                                {
                                    debug("Unable to parse <deleteArchiveDays> value, not a number", "info");
                                }
                            }
                            timer.Stop();
                            debug("Archived " + iArchivedCount + " item(s) and deleted " + iArchivesDeletedCount + " archive(s) in " + timerDisplay(timer.ElapsedMilliseconds), "info");
                        }
                        else
                        {
                            Console.WriteLine("Invalid parameters in configuration file", "info");
                        }
                    }
                    catch (Exception ex)
                    {
                        debug("Error parsing XML Document " + ex.ToString(), "info");
                    }
                }
            } catch
            {
                debug("Error loading XML Document.  Ensure that " + System.AppDomain.CurrentDomain.BaseDirectory + "\\config\\paths.xml exists", "info");
            }       
        }
        //Validate paramaters
        private static bool validParams(string location, string archiveDays)
        {
            bool valid = true;
            int ad;
            if (!objExists(location))
                valid = false;
            if (!int.TryParse(archiveDays, out ad))
                valid = false;
            return valid;
        }
        private static void addFileToArchive(string file, string archive, string archivePath, bool deleteOriginal, bool dryRun)
        {
            //Check if the user specified an archiveDirectory in config file.  If so, append it to the local path of the log files
            archivePath = (!String.IsNullOrEmpty(archivePath)) ? Path.GetDirectoryName(file) + "\\" + archivePath : Path.GetDirectoryName(file);
            archive = archivePath + "\\" + archive;
            if (!objExists(archivePath))            
                Directory.CreateDirectory(archivePath);            
            if (dryRun)
                debug("Dry Run, not modifying file " + file, "info");
            else
            {
                using (ZipFile z = new ZipFile())
                {
                    z.AddFile(file).FileName = Path.GetFileName(file);
                    z.Save(archive);
                }
                if (deleteOriginal) 
                    deleteFile(file);
                debug("Adding file: " + file + " to archive " + archive, "info");
                
            }
        }
        private static void deleteFile(string file)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                debug("Error deleting file " + file + ": " + ex.ToString(), "error");
            }
        }
        //Checks if an object exists
        private static bool objExists(string objPath)
        {
            try
            {
                if (isDirectory(objPath))
                {
                    debug("Object is a directory", "info");
                    return true;
                }
                else
                {
                    if (File.Exists(objPath))
                    {
                        debug("Object is a file.  Filters will be removed", "info");
                        return true;
                    }
                    else
                    {
                        debug("Object does not exist", "info");
                        return false;
                    }
                }
            }
            catch
            {
                debug("Fell through try catch on objExists method", "info");
                return false;
            }
        }
        //Check if object is directory
        private static bool isDirectory(string objPath)
        {
            try
            {
                bool isDir = (File.GetAttributes(@objPath) & FileAttributes.Directory) == FileAttributes.Directory;
                return isDir;
            }
            catch
            {
                return false;
            }
        }
        private static void DeleteArchives(string parent, IList<string> archives, int days, bool dryRun)
        {
            //If dryRun, show files that will be deleted, but don't delete them
            if (dryRun)
            {
                Directory.GetFiles(parent)
                    .Select(f => new FileInfo(f))                    
                    .Where(f => f.Extension == ".zip" && f.LastWriteTime < DateTime.Now.AddDays(-days))
                    .ToList()
                    .ForEach(s => debug("Dry Run, would delete: " + s.FullName, "info"));
            } else
            {
                Directory.GetFiles(parent)
                    .Select(f => new FileInfo(f))
                    .Where(f => f.Extension == ".zip" && f.LastWriteTime < DateTime.Now.AddDays(-days))
                    .ToList()
                    .ForEach(s => { iArchivesDeletedCount++; debug("Deleting archive: " + s, "info"); s.Delete(); });
            }
            if (bRecursive)
                Directory.GetDirectories(parent).ToList().ForEach(s => DeleteArchives(s, archives, days, dryRun));
        }
        //Safe way to recursively search a directory and avoid access denied errors
        private static IList<string> GetFiles(string parent, IList<string> files, int days, bool fileExtensionFilter, List<string> strIncludeExtension)
        {
            try
            {
                /*
                Directory.GetFiles(parent).ToList().Select(f => new FileInfo(f)).Where(f => f.LastAccessTime < DateTime.Now.AddDays(-3)).ForEach(s => files.Add(s));
                if (bRecursive)
                    Directory.GetDirectories(parent).ToList().ForEach(s => GetFiles(s, files));
                */
                //string[] ext = { ".log", ".txt", ".aes" };
                if (fileExtensionFilter)
                {
                    Directory.GetFiles(parent)
                        .Select(f => new FileInfo(f))
                        .Where(f => strIncludeExtension.Contains(f.Extension) && f.LastAccessTime < DateTime.Now.AddDays(-days))
                        .ToList()
                        .ForEach(s => files.Add(s.FullName));
                } else
                {
                    Directory.GetFiles(parent)
                        .Select(f => new FileInfo(f))
                        .Where(f => f.LastAccessTime < DateTime.Now.AddDays(-days))
                        .ToList()
                        .ForEach(s => files.Add(s.FullName));
                }
                if (bRecursive)
                    Directory.GetDirectories(parent).ToList().ForEach(s => GetFiles(s, files, days, fileExtensionFilter, strIncludeExtension));
            }
            catch (UnauthorizedAccessException ex)
            {
                iAccessDenied++;
            }
            catch
            {
                //Usually indicates this is a file and not a directory
                files.Add(parent);
            }
            return files;
        }
        //Debug strings
        private static void debug(string str, string logType)
        {
            if (bDebug)
                Console.WriteLine(str);
            switch(logType)
            {
                case "info":
                    log.Info(str);
                    break;
                case "debug":
                    log.Debug(str);
                    break;
                case "error":
                    log.Error(str);
                    break;
                default:
                    log.Info(str);
                    break;
            }
        }
        //Quick and dirty way to convert milliseconds to seconds/minutes
        //This is only called if debug is enabled
        private static string timerDisplay(double ms)
        {
            if (ms < 1000) //Return MS
            {
                return ms + "ms";
            }
            else if (ms >= 1000 && ms < 60000)
            {  //Use seconds 
                return Convert.ToString(TimeSpan.FromMilliseconds(ms).TotalSeconds) + "s";
            }
            else //Return minutes
            {
                return Convert.ToString(TimeSpan.FromMilliseconds(ms).TotalMinutes) + "m";
            }
        }
    }
}
