﻿//2018.12.04, czs, create in hmx, 网平差单独移出来

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geo;
using Geo.IO;
using System.IO;
using Geo.Coordinates;
using Geo.Times;
using Geo.Algorithm;
using Geo.Algorithm.Adjust;

namespace Gnsser
{


    /// <summary>
    /// 基线平差类型
    /// </summary>
    public enum BaselineNetAdjustType
    {
        秩亏自由网平差,
        固定基准站约束平差
    }


    /// <summary>
    /// 拟稳平差
    /// </summary>
    public class QuasiStableFreeBaselineNetMatrixBuilder : RankDeficiencyBaselineNetMatrixBuilder
    {
        public QuasiStableFreeBaselineNetMatrixBuilder(List<EstimatedBaseline> independetnLines, List<string> totalSites, List<string> FixedSiteNames) : base(independetnLines, totalSites, FixedSiteNames)
        {
        }
        public override void Build()
        {
            Matrix GGT = BuildGGT();

            CoeffIncrementOfNormalEquation = GGT;


            base.Build();
        }

        private Matrix BuildGGT()
        {
            var siteCount = SiteCount;
            int size = siteCount * 3;
            DiagonalMatrix diagonal = new DiagonalMatrix(size, 0);
            foreach (var item in FixedSiteNames) //固定
            {
                int index = this.SiteNames.IndexOf(item);
                diagonal[index + 0] = 1;
                diagonal[index + 1] = 1;
                diagonal[index + 2] = 1;
            }
            var fixSiteCount = FixedSiteNames.Count;
            var factor = 1.0 / Math.Sqrt(fixSiteCount);
            Matrix G = factor * new Matrix(diagonal);

            var GGT = G * G.Trans;
            return GGT;
        }
        private Matrix BuildG2G2T()
        {
            var siteCount = SiteCount;
            int size = siteCount * 3;
            DiagonalMatrix diagonal = new DiagonalMatrix(size, 1);
            var fixSiteCount = FixedSiteNames.Count;
            var factor = 1.0 / Math.Sqrt(fixSiteCount);
            Matrix G = factor * new Matrix(diagonal);

            var GGT = G * G.Trans;
            return GGT;
        }
    }

    /// <summary>
    /// 秩亏自由网平差
    /// </summary>
    public class FreeRankDeficiencyBaselineNetMatrixBuilder : RankDeficiencyBaselineNetMatrixBuilder
    {
        public FreeRankDeficiencyBaselineNetMatrixBuilder(List<EstimatedBaseline> independetnLines, List<string> totalSites, List<string> FixedSiteNames) : base(independetnLines, totalSites, FixedSiteNames)
        {
        }

        public override void Build()
        {
            Matrix GGT = BuildGGT();

            CoeffIncrementOfNormalEquation = GGT;


            base.Build();
        }

        private Matrix BuildGGT()
        {
            var siteCount = SiteCount;
            int size = siteCount * 3;
            DiagonalMatrix diagonal = new DiagonalMatrix(size, 1);
            var factor = 1.0 / Math.Sqrt(siteCount);
            Matrix G = factor * new Matrix(diagonal);
            var GGT = G * G.Trans;
            return GGT;
        }
    }

    /// <summary>
    /// 秩亏基线网平差矩阵生成器，将条件增加到法方程上。
    /// </summary>
    public abstract class RankDeficiencyBaselineNetMatrixBuilder : BaseAdjustMatrixBuilder
    {
        public RankDeficiencyBaselineNetMatrixBuilder(List<EstimatedBaseline> independetnLines, List<string> totalSites, List<string> FixedSiteNames)
        {
            SiteNames = totalSites;
            if (FixedSiteNames == null) { FixedSiteNames = new List<string>(); }
            if (FixedSiteNames.Count == 0)
            {
                FixedSiteNames.Add(SiteNames.First());
            }
            this.FixedSiteNames = FixedSiteNames;
            this.IndependentLines = independetnLines;
            this.ParamNames = new List<string>();
            foreach (var item in this.SiteNames)
            {
                ParamNames.Add(item + "_" + Gnsser.ParamNames.Dx);
                ParamNames.Add(item + "_" + Gnsser.ParamNames.Dy);
                ParamNames.Add(item + "_" + Gnsser.ParamNames.Dz);
            }
        }
        /// <summary>
        /// 测站名称 
        /// </summary>
        public List<string> SiteNames { get; set; }

        /// <summary>
        /// 固定坐标的测站
        /// </summary>
        public List<string> FixedSiteNames { get; set; }

        /// <summary>
        /// 独立基线
        /// </summary>
        public List<EstimatedBaseline> IndependentLines { get; set; }
        /// <summary>
        /// 测站数量
        /// </summary>
        public int SiteCount => this.SiteNames.Count;

        /// <summary>
        /// (观测值数量为基线 + 固定测站数量) 乘以 3 , 
        /// </summary>
        public override int ObsCount => (IndependentLines.Count) * 3;
        /// <summary>
        /// 参数数量，为测站数量乘以 3 
        /// </summary>
        public override int ParamCount => base.ParamCount;

        public override WeightedVector AprioriParam => null;

        public override bool IsParamsChanged => false;

        public override void Build()
        {

            base.Build();
        }

        /// <summary>
        /// 只有基线系数阵
        /// </summary>
        public override Matrix Coefficient
        {
            get
            {
                Matrix coef = new Matrix(ObsCount, ParamCount);
                int i = 0;
                foreach (var line in IndependentLines)
                {
                    var indexOfRef = this.SiteNames.IndexOf(line.BaseLineName.RefName);
                    var indexOfRov = this.SiteNames.IndexOf(line.BaseLineName.RovName);

                    int startOfRef = indexOfRef * 3;
                    int startOfRov = indexOfRov * 3;

                    coef[i + 0, startOfRef + 0] = -1;
                    coef[i + 1, startOfRef + 1] = -1;
                    coef[i + 2, startOfRef + 2] = -1;

                    coef[i + 0, startOfRov + 0] = 1;
                    coef[i + 1, startOfRov + 1] = 1;
                    coef[i + 2, startOfRov + 2] = 1;

                    i = i + 3;
                }
                return coef;
            }
        }

        public override WeightedVector Observation
        {
            get
            {
                //构建 L 矩阵
                Vector observation = new Vector(ObsCount);
                int i = 0;
                foreach (var line in IndependentLines)
                {
                    var approx = line.ApproxVector;
                    var obs = line.EstimatedVector;
                    var delta = obs - approx;
                    observation[i++] = delta.X;
                    observation[i++] = delta.Y;
                    observation[i++] = delta.Z;
                }

                Matrix cova = new Matrix(new SymmetricMatrix(ObsCount, 0));
                i = 0;
                foreach (var line in IndependentLines)
                {
                    var approxXyz = line.ApproxVector;
                    var startIndex = i * 3;
                    cova.SetSub(line.CovaMatrix, startIndex, startIndex);
                    i++;
                }

                WeightedVector vs = new WeightedVector(observation, cova);
                vs.ParamNames = BuildObsNames();
                return vs;
            }
        }

        /// <summary>
        /// 构建观测值名称
        /// </summary>
        /// <returns></returns>
        public List<string> BuildObsNames()
        {
            List<string> names = new List<string>();
            int i = 0;
            foreach (var line in IndependentLines)
            {
                names.Add(line.Name + Gnsser.ParamNames.Divider + Gnsser.ParamNames.Dx);
                names.Add(line.Name + Gnsser.ParamNames.Divider + Gnsser.ParamNames.Dy);
                names.Add(line.Name + Gnsser.ParamNames.Divider + Gnsser.ParamNames.Dz);
            }
            return names;
        }

    }

}