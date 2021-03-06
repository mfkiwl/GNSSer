﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Geo.Coordinates;
using Geo.Utils;
using Gnsser.Times;
using Geo.Times; 

namespace Gnsser.Interoperation.Bernese
{
    /// <summary>
    /// Bernese 坐标文件。
    /// </summary>
    public class VelFile : IBerFile
    {
        /// <summary>
        /// 说明或其它信息。
        /// </summary>
        public List<string> Comments { get; set; }
        /// <summary>
        /// 标签
        /// </summary>
        public string Label { get; set; }
        /// <summary>
        /// 日期字符串
        /// </summary>
        public string DateString { get; set; }
        /// <summary>
        /// 历元。
        /// </summary>
        public Time Epoch { get; set; }
        /// <summary>
        /// 基准
        /// </summary>
        public string Datum { get; set; }

        /// <summary>
        /// 坐标集合
        /// </summary>
        public List<VelItem> Items { get; set; }

        /// <summary>
        /// 统计条目数量
        /// </summary>
        public int Count { get { return Items.Count; } }

        /// <summary>
        /// 添加。
        /// </summary>
        /// <param name="key"></param>
        public void Add(VelItem item)
        {
            if (!Items.Contains(item))
            {
                Items.Add(item);
            }
        }

        /// <summary>
        /// 将另一个文件的内容添加进来，两个文件合并成一个文件。
        /// </summary>
        /// <param name="file"></param>
        /// <param name="strict"></param>
        /// <returns></returns>
        public void Merge(VelFile file, bool strict = false)
        {
            if (strict && (Datum != file.Datum || !this.Epoch.Equals(file.Epoch))) throw new ArgumentException("两个文件历元或基准不一样。");
            int num = this.Items.Count;
            foreach (var item in file.Items)
            {
                if (!Items.Contains(item))
                {
                    item.Num += num;
                    this.Items.Add(item);
                }
            }
        }

        /// <summary>
        /// 写入文件。
        /// </summary>
        /// <param name="path"></param>
        public void Save(string path)
        {
            using (StreamWriter writer = new StreamWriter(path, false))
            {
                writer.Write(ToString());
            }
        }
        /// <summary>
        /// 字符串
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            string line = "";
            line = Label;
            line = StringUtil.FillSpace(line, 66);
            line += DateString;
            sb.AppendLine(line);
            sb.AppendLine("--------------------------------------------------------------------------------");
            line = "LOCAL GEODETIC DATUM: " + this.Datum;
            line = StringUtil.FillSpace(line, 40);
            line += "EPOCH: " + this.Epoch.ToString();
            sb.AppendLine(line);
            sb.AppendLine();
            sb.AppendLine("NUM  STATION NAME           VX (M/Y)       VY (M/Y)       VZ (M/Y)  FLAG   PLATE");
            sb.AppendLine();
            foreach (VelItem coord in Items)
            {
                sb.AppendLine(coord.ToLine());
            }

            string s = sb.ToString();
            return s;
        }
         

         /// <summary>
        /// 读取文本。
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static VelFile ParseText(string text)
        {
            using (MemoryStream stream = new MemoryStream(ASCIIEncoding.ASCII.GetBytes(text)))
            {
                return Read(stream);
            }
        }

        /// <summary>
        /// 读取Bernese速度文件
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static VelFile Read(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open))
            {
                return Read(stream);
            }
        }         

        /// <summary>
        /// 读取Bernese速度文件
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static VelFile Read(Stream stream)
        {
            VelFile file = new VelFile();
            if (file.Comments == null) file.Comments = new List<string>();

            using (StreamReader reader = new StreamReader(stream))
            {
                string line;
                //第一行
                //PPP_021430_143MATE                                               24-JAN-13 09:21
                line = reader.ReadLine();
                file.Label = line.Substring(0, 65).Trim();
                file.DateString = line.Substring(65).Trim();

                //第二行
                //--------------------------------------------------------------------------------
                line = reader.ReadLine();

                //第3行
                //LOCAL GEODETIC DATUM: IGS00             EPOCH: 2002-05-23 11:57:30
                line = reader.ReadLine();
                if (line.Contains("DATUM"))
                {
                    file.Datum = line.Substring(line.IndexOf(":") + 1, 15).Trim();
                }
                if (line.Contains("EPOCH"))
                {
                    file.Epoch = new Time(DateTime.Parse(line.Substring(line.IndexOf("EPOCH") + 6)));
                }

                while ((line = reader.ReadLine()) != null)
                {
                    //正式记录
                    if (line.StartsWith("NUM"))
                    {
                        file.Items = new List<VelItem>();
                        line = reader.ReadLine();//空了一行。
                        while (line != null)
                        {
                            line = line.Trim();
                            if (line == "")
                            {
                                line = reader.ReadLine();
                                continue;
                            }

                            VelItem coord = VelItem.ParseLine(line);
                            file.Items.Add(coord);

                            line = reader.ReadLine();
                            if (line == "") break;//跳出
                        }

                        //末尾有些东西
                        while ((line = reader.ReadLine()) != null)
                        {
                            file.Comments.Add(line);

                        }
                    }
                }
                return file;
            }
        }

        /// <summary>
        /// 从观测文件夹中读取。
        /// </summary>
        /// <param name="oDir"></param>
        /// <returns></returns>
        public static VelFile CreateFromODir(string oDir)
        {
            VelFile file = new VelFile()
            {
                Comments = new List<string>(),
                DateString =DateTime.Now + "",
                Datum = "IGS00",
                Epoch = Time.Parse("2000-01-01 00:00:00"),
                Items = new List<VelItem>(),
                Label = "NUVEL1A-NNR VELOCITIES  "
            };

            file.Items = new List<VelItem>();
            string[] files = Directory.GetFiles(oDir, Setting.RinexOFileFilter);
            int num = 1;
            foreach (var item in files)
            {
                Data.Rinex.RinexObsFileHeader h = new Data.Rinex.RinexObsFileReader(item).GetHeader();
                //判断是否已经存在。
                string makerName = StringUtil.FillZero(h.MarkerName.ToUpper(), 4).Substring(0, 4);
                if (file.Items.Find(m => m.StationName == makerName) != null) continue;

                string code = h.SiteInfo.MarkerNumber;
                file.Items.Add(new VelItem()
                {
                    Code = code,
                    Flag = "",
                    Num = num++,
                    Plate = "",
                    StationName = makerName,
                    Vxyz = new XYZ()
                });
            }

            return file;
        }

        public static IBerFile Merger(VelFile one, VelFile another)
        {
            VelFile newOne = new VelFile();
            newOne.DateString = DateTime.Now + "";
            newOne.Datum = one.Datum;
            newOne.Epoch = one.Epoch;
            newOne.Label = "Merged";            
            newOne.Items = new List<VelItem>();
            newOne.Items.AddRange(one.Items);

            foreach (var item in another.Items)
                newOne.Add(item);

            return newOne;
        }

         
    }

}
