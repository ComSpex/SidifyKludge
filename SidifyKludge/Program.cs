﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FileFinder;

// Sidifyのバグの後始末をするプログラム（要は、13Jun2018の.wavは削除したい。しかし、問題の無いファイルも混ざっておる。それらの特定が・・・）。
namespace SidifyKludge {
	class Program {
		static StreamWriter output;
		static StreamWriter movers;
		static bool isMove = false;
		static bool isOfDate = false;
		static bool isSidify = false;
		static string DestFolder;
		static DirectoryInfo sidifyOut = null;
		static DateTime ofDate = DateTime.MinValue;
		static void Main(string[] args) {
			string MyDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
			isMove=Environment.CommandLine.Contains("--move");
			isOfDate = Environment.CommandLine.Contains("--ofdate;");
			isSidify=Environment.CommandLine.Contains("--sidifykludge;");
			if(isMove) {
				string[] elems = Environment.CommandLine.Split(' ');
				DestFolder=Path.Combine(MyDocuments,Path.GetFileNameWithoutExtension(elems[0].Substring(1,elems[0].Length-2)));
				DestFolder=String.Format("{0}_{1}",DestFolder,DateTime.Today.ToString("ddMMMyyyy"));
				Directory.CreateDirectory(DestFolder);
			}
			if(isOfDate) {
				string[] argus = Environment.GetCommandLineArgs();
				foreach(string arg in argus) {
					if(arg.ToLower().Contains("--ofdate;")) {
						string[] pair = arg.Split(';');
						if(pair.Length==2) {
							if(DateTime.TryParse(pair[1],out ofDate)) {
								break;
							}
						}
					}
				}
			}
			if(isSidify) {
				string[] argus = Environment.GetCommandLineArgs();
				foreach(string arg in argus) {
					if(arg.ToLower().Contains("--sidifykludge;")) {
						string[] pair = arg.Split(';');
						if(pair.Length==2) {
							sidifyOut = new DirectoryInfo(pair[1]);
							break;
						}
					}
				}
			}
			for(int i = 0;i<args.Length;++i) {
				if(args[i].StartsWith("--")) {
					continue;
				}
				FileInfo outtext = new FileInfo(String.Format("output_{0}.txt",i));
				if(outtext.Exists) {
					if(outtext.Length>0) {
						WinAPI.MoveToRecycleBin(outtext);
					} else {
						outtext.Delete();
					}
				}
				FileInfo moveout = new FileInfo(String.Format("movers_{0}.txt",i));
				if(moveout.Exists) {
					if(moveout.Length>0) {
						WinAPI.MoveToRecycleBin(moveout);
					} else {
						moveout.Delete();
					}
				}
				if(isMove) {
					movers=new StreamWriter(moveout.FullName) { AutoFlush=true };
				}
				using(output=new StreamWriter(outtext.FullName) { AutoFlush=true }) {
					if(isSidify&&sidifyOut!=null) {
						string pattern = "*.wav";
						int index = 0;
						Debug.Assert(String.IsNullOrEmpty(args[index])==false);
						pattern=args[index];
						Traverse(sidifyOut,pattern);
					} else {
						Traverse(args[i]);
					}
					Report("{0}",new String('-',80));
				}
			}
			if(movers!=null) {
				movers.Close();
			}
		}
		private static void Traverse(string fileSpec) {
			DriveInfo[] drives = DriveInfo.GetDrives();
			foreach(DriveInfo drive in drives) {
				Traverse(drive,fileSpec);
			}
		}
		private static void Traverse(DriveInfo drive,string fileSpec) {
			string name = drive.Name;
			DirectoryInfo root = new DirectoryInfo(name);
			Traverse(root,fileSpec.Replace('"','\0'));
		}
		private static void Traverse(DirectoryInfo folder,string fileSpec) {
			if(isMove) {
				if(Contained(folder,DestFolder)) {
					return;
				}
			}
			try {
				if(Contained(folder,fileSpec)) {
					Report(folder);
				}
			} catch { }
			try {
				DirectoryInfo[] dirs = folder.GetDirectories();
				foreach(DirectoryInfo dir in dirs) {
					Traverse(dir,fileSpec);
				}
				if(Environment.CommandLine.Contains("--folder")) {
					return;
				}
				bool isPartial = Environment.CommandLine.Contains("--partial");
				bool isFileName = Path.GetExtension(fileSpec).Length>0;
				bool isPartialFileName = (!isPartial)&isFileName;
				string spec = isPartial ? String.Format(isPartialFileName ? "*{0}" : "*{0}*",fileSpec) : fileSpec;
				FileInfo[] files = folder.GetFiles(spec);
				if(files.Length>0) {
					foreach(FileInfo file in files) {
						Traverse(file,fileSpec);
						if(isMove) {
							Debug.Assert(movers!=null);
							if(!AlreadyExists(file,out string newfile)) {
								if(isOfDate){
									if(HandleOfDate(file)) {
										movers.WriteLine("'{0}','{1}'",newfile,file.FullName);
										file.MoveTo(newfile);
									}
								} else {
									file.MoveTo(newfile);
								}
							} else {
								WinAPI.MoveToRecycleBin(file);
							}
						}
					}
				}
			} catch(Exception ex) {
				Report(ex);
			}
		}
		private static bool HandleOfDate(FileInfo sourfile) {
			if(isOfDate&&ofDate!=DateTime.MinValue) {
				if(sourfile.CreationTime.Date==ofDate) {
					return true;
				}
			}
			return false;
		}
		private static bool AlreadyExists(FileInfo sourfile,out string newfile) {
			FileInfo destfile = new FileInfo(Path.Combine(DestFolder,Path.GetFileName(sourfile.FullName)));
			newfile=destfile.FullName;
			if(destfile.Exists) {
				bool isTrue =
					destfile.FullName==sourfile.FullName&&
					destfile.LastAccessTime==sourfile.LastAccessTime&&
					destfile.Length==sourfile.Length;
				return isTrue;
			}
			return false;
		}
		private static bool Contained(DirectoryInfo folder,string fileSpec) {
			string lhs = folder.FullName.ToLower();
			string rhs = fileSpec.ToLower();
			return lhs.Contains(rhs);
		}

		private static void Traverse(FileInfo file,string fileSpec) {
			if(isOfDate) {
				if(HandleOfDate(file)) {
					if(SidifyBug(file,out bool showFolder)) {
						WinAPI.MoveToRecycleBin(file);
						Report(file,showFolder);
					}
				}
			} else {
				Report(file);
			}
		}

		private static bool SidifyBug(FileInfo file,out bool isTrue) {
			string ext = Path.GetExtension(file.FullName);
			isTrue=ext.ToLower()=="wav.wav";
			if(!isTrue) {
				string[] words = Path.GetFileNameWithoutExtension(file.FullName).Split();
				if(words.Length==1) {
					DirectoryInfo folder = new DirectoryInfo(Path.GetDirectoryName(file.FullName));
					FileInfo[] files = folder.GetFiles(String.Format("{0}*",words[0]));
					isTrue = files.Length>=2;
				}
			}
			return isTrue;
		}

		private static void Report(Exception ex) {
			Console.ForegroundColor=ConsoleColor.Red;
			Report("{0}",ex.Message);
			Console.ResetColor();
		}
		private static void Report(DirectoryInfo file) {
			DateTime from = file.CreationTime;
			DateTime upto = file.LastWriteTime;
			if(from>upto) {
				swap(ref from,ref upto);
			}
			Uri url = new Uri(file.FullName);
			string ftex = Uri.UnescapeDataString(url.AbsoluteUri);
			ftex=Uri.UnescapeDataString(ftex);
			string text = String.Format(@"{0:ddMMMyyyy} | {1:ddMMMyyyy} | ""{2}""",from,upto,ftex);
			output.WriteLine(text);
			Report("{0}",text);
		}
		private static void Report(FileInfo file,bool showFolder=false) {
			DateTime from = file.CreationTime;
			DateTime upto = file.LastWriteTime;
			if(from>upto) {
				swap(ref from,ref upto);
			}
			string text = String.Empty;
			if(showFolder) {
				Uri url = new Uri(Path.GetDirectoryName(file.FullName));
				string ftex = Uri.UnescapeDataString(url.AbsoluteUri);
				ftex=Uri.UnescapeDataString(ftex);
				text = String.Format(@"{0:ddMMMyyyy} | {1:ddMMMyyyy} | {3,15:###,###,###,###,###} | ""{4}"" | {2}",from,upto,file.FullName,file.Length,ftex);
			} else {
				text = String.Format("{0:ddMMMyyyy} | {1:ddMMMyyyy} | {3,15:###,###,###,###,###} | {2}",from,upto,file.FullName,file.Length);
			}
			if(!String.IsNullOrEmpty(text)) {
				output.WriteLine(text);
				Report("{0}",text);
			}
		}
		private static void swap(ref DateTime from,ref DateTime upto) {
			DateTime keep = from;
			from=upto;
			upto=keep;
		}
		private static void Report(DriveInfo drive) {
			Report("{0}",drive.Name);
		}
		private static string Report(string format,params object[] args) {
			string text = String.Format(format,args);
			Console.WriteLine(text);
			return text;
		}
		private static bool IsOfWhat(FileInfo file,FileAttributes attr) {
			return (file.Attributes&attr)==attr;
		}
		private static bool IsDirectory(FileInfo file) {
			return IsOfWhat(file,FileAttributes.Directory);
		}
	}
}
