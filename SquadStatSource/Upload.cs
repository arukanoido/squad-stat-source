using System;
using System.IO;
using System.Collections.Generic;
using Amazon.S3;
using Amazon.S3.Transfer;
using Newtonsoft.Json.Linq;

namespace SquadStatSource
{
    public class Uploader
    {
        public string AWSAccessKey { get; }
        public string AWSSecretKey { get; }

        public IAmazonS3 S3 { get; }

        FileSystemWatcher Watcher = new FileSystemWatcher();

        public List<string> FilesUploading = new List<string>(); 
        
        string Base = "matchdata\\";

        public Uploader(JObject Config)
        {
            AWSAccessKey = Config["aws_access_key"].ToString();
            AWSSecretKey = Config["aws_secret_key"].ToString();

            S3 = new AmazonS3Client(AWSAccessKey, AWSSecretKey, Amazon.RegionEndpoint.USWest2);

#if DEBUG
            Base = "..\\match\\";
#endif
#if RELEASE
            System.IO.Directory.CreateDirectory("matchdata");
#endif
        }

        public void Upload(string Path, string FileName, string Partition)
        {
            var Utility = new TransferUtility(S3);
            var Request = new TransferUtilityUploadRequest
            {
                BucketName = "squadstats-raw",
                Key = Partition + FileName,
                FilePath = Path + "\\" + FileName,
            };
            var UploadTask = Utility.UploadAsync(Request);
            UploadTask.ContinueWith(Result =>
            {
                if (Result.Status == System.Threading.Tasks.TaskStatus.Faulted)
                {
                    FilesUploading.Remove(System.IO.Path.GetFileNameWithoutExtension(FileName));
                    Console.WriteLine(Result.Exception);
                }
                else if (Result.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
                {
                    File.Delete(Path + "\\" + FileName);
                    Console.WriteLine("Uploaded " + FileName);
                }
            });
        }

        public void UploadDiff(string Partition)
        {
            string[] FilePaths = Directory.GetFiles(Base, "*.parquet", SearchOption.TopDirectoryOnly);
            foreach (var FilePath in FilePaths)
            {
                if (!FilesUploading.Contains(System.IO.Path.GetFileNameWithoutExtension(FilePath)))
                {
                    FilesUploading.Add(System.IO.Path.GetFileNameWithoutExtension(FilePath));
                    Upload(System.IO.Path.GetDirectoryName(FilePath), System.IO.Path.GetFileName(FilePath), Partition);
                }
            }
        }
    }
}