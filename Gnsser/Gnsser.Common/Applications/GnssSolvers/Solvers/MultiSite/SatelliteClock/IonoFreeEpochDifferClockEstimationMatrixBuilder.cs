﻿//2016.10.05 double creates in Quxian
//2016.10.21, double, edit in hongqing, 修改矩阵。


using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Geo.Algorithm;
using Geo.Utils;
using Geo.Common;
using Geo.Algorithm;
using Geo.Coordinates;
using Geo.Algorithm.Adjust;
using Gnsser.Times;
using Gnsser.Domain;
using Gnsser.Service;
using Gnsser.Data.Rinex;
using Gnsser.Checkers;
using Gnsser.Models;
using Geo.Times;
using Geo;

namespace Gnsser
{
    class IonoFreeEpochDifferClockEstimationMatrixBuilder : AbstractMultiSitePeriodMatrixBuilder
    {
        /// <summary>
        /// 精密单点定位矩阵生成类 构造函数。
        /// </summary>
        /// <param name="option">解算选项</param>
        public IonoFreeEpochDifferClockEstimationMatrixBuilder(
            GnssProcessOption option)
            : base(option)
        {
            this.Option = option;
            this.SatWeightProvider = new SatElevateWeightProvider(option);
            this.ParamNameBuilder = new EpochDifferClockParamNameBuilder(option);

            //SetStateModel();
        }


        #region 全局基础属性

        public override bool IsParamsChanged
        {
            get
            {
                if (PreviousProduct != null)
                {
                    List<string> ParamNames1 = ParamNames;
                    List<string> ParamNames2 = PreviousProduct.ParamNames;
                    return !Geo.Utils.ListUtil.IsEqual(ParamNames1, ParamNames2);
                }
                return true;
            }
        }


        public override List<string> BuildParamNames()
        {
            var nameBuilder = (EpochDifferClockParamNameBuilder)this.ParamNameBuilder;
            nameBuilder.PeriodInfos = CurrentMaterial;
            nameBuilder.SatelliteNumbers = EnabledPrns;
            this.ParamNames = nameBuilder.Build();
            return ParamNames;
        }

        public override void UpdateStateTransferModels()
        {
            var nameBuilder = (EpochDifferClockParamNameBuilder)this.ParamNameBuilder;

            foreach (var epoch in CurrentMaterial.First)
            {
                var key = nameBuilder.GetReceiverClockParamName(epoch);
                if (!ParamStateTransferModelManager.Contains(key))
                {
                    ParamStateTransferModelManager.Add(key,  new WhiteNoiseModel(Option.StdDevOfRevClockWhiteNoiseModel));
                }
            }

            foreach (var epoch in CurrentMaterial.First)
            {
                var key = nameBuilder.GetSiteWetTropZpdName(epoch);
                if (!ParamStateTransferModelManager.Contains(key))
                {
                    ParamStateTransferModelManager.Add(key, new RandomWalkModel(Option.StdDevOfTropoRandomWalkModel));
                }

            }

            foreach (var epoch in CurrentMaterial.First)
            {
                foreach (var sat in EnabledPrns)
                {
                    var key = nameBuilder.GetSatClockParamName(sat);
                    if (!ParamStateTransferModelManager.Contains(key))
                    {
                        ParamStateTransferModelManager.Add(key,  new WhiteNoiseModel(Option.StdDevOfRevClockWhiteNoiseModel));
                    }

                }
            }
        }
        /// <summary>
        /// 参考站信息
        /// </summary>
        MultiSiteEpochInfo CurrentInfo { get { return CurrentMaterial.First; } }

        MultiSiteEpochInfo PreviousInfo { get { return CurrentMaterial[1]; } }
        /// <summary>
        /// 判断基准星是否发生变化（与之前的比较）
        /// </summary>
        public bool isBasePrnChange { get; set; }

        /// <summary>
        /// 基础卫星编号。都以此卫星的单差做差分。
        /// </summary>
        public SatelliteNumber BasePrn { get; private set; }

        /// <summary>
        /// 通过此设置，可以判断是否改变
        /// </summary>
        /// <param name="BasePrn"></param>
        public IonoFreeEpochDifferClockEstimationMatrixBuilder SetBasePrn(SatelliteNumber BasePrn)
        {
            isBasePrnChange = (this.BasePrn != BasePrn);

            this.BasePrn = BasePrn;
            return this;
        }
        /// <summary>
        /// 卫星权值提供者
        /// </summary>
        //public ISatWeightProvider SatWeightProvider { get; set; }
        /// <summary>
        /// 参数数量
        /// </summary>
        public override int ParamCount
        {
            get
            {
                int ParamCount = 0;
                ParamCount = ObsCount + 2 * CurrentMaterial.Count + EnabledPrns.Count;
                return ParamCount;
            }
        }


        /// <summary>
        /// 观测数量
        /// </summary>
        public override int ObsCount
        {
            get
            {
                int obsCount = 0;
                foreach (var EpochInfo in CurrentMaterial)
                {
                    obsCount += EpochInfo.EnabledSatCount;
                }
                return obsCount;
            }
        }
        #endregion


        #region 参数先验信息        
        /// <summary>
        /// 创建初始先验参数值和协方差阵。只会执行一次。
        /// </summary>
        /// <returns></returns>
        protected override WeightedVector CreateInitAprioriParam()
        {
            return ClockEstimationMatrixHelper.GetInitEpochDifferAprioriParam(this.ParamCount, this.CurrentMaterial.Count, EnabledPrns.Count);
        }
        #endregion

        #region 创建观测信息

        /// <summary>
        /// 具有权值的观测值。
        /// </summary> 
        public override WeightedVector Observation
        {
            get
            {
                IMatrix inverseWeightOfObs = BulidInverseWeightOfObs();
                WeightedVector deltaObs = new WeightedVector(ObservationVector, inverseWeightOfObs);
                return deltaObs;
            }
        }

        /// <summary>
        /// 观测值。
        /// 自由项 l，观测值减去先验值或估计值。
        /// 常数项，观测误差方程的常数项,或称自由项
        /// </summary>
        public virtual IVector ObservationVector
        {
            get
            {
                Vector L = new Vector(ObsCount * 2);
                int satIndex = 0;
                foreach (var epoch in CurrentInfo)
                {
                    XYZ staXyz = epoch.SiteInfo.ApproxXyz;//使用周解或日解坐标

                    Vector rangeVector = epoch.GetAdjustVector(SatObsDataType.IonoFreeRange);
                    Vector phaseVector = null;
                    if (this.Option.IsAliningPhaseWithRange)
                    {
                        phaseVector = epoch.GetAdjustVector(SatObsDataType.AlignedIonoFreePhaseRange, true);
                    }
                    else
                    {
                        phaseVector = epoch.GetAdjustVector(SatObsDataType.IonoFreePhaseRange, true);
                    }
                    int index = 0;
                    foreach (var item in epoch.EnabledSats)
                    {
                        L[satIndex] = rangeVector[index];
                        L[satIndex + ObsCount] = phaseVector[index];
                        satIndex++;
                        index++;
                    }
                }
                foreach (var epoch in PreviousInfo)
                {
                    XYZ staXyz = epoch.SiteInfo.ApproxXyz;//使用周解或日解坐标

                    Vector rangeVector2 = epoch.GetAdjustVector(SatObsDataType.IonoFreeRange);
                    Vector phaseVector2 = null;
                    if (this.Option.IsAliningPhaseWithRange)
                    {
                        phaseVector2 = epoch.GetAdjustVector(SatObsDataType.AlignedIonoFreePhaseRange, true);
                    }
                    else
                    {
                        phaseVector2 = epoch.GetAdjustVector(SatObsDataType.IonoFreePhaseRange, true);
                    }
                    
                    int index = 0;
                    foreach (var item in epoch.EnabledSats)
                    {
                        L[satIndex] -= rangeVector2[index];
                        L[satIndex + ObsCount] -= phaseVector2[index];
                        satIndex++;
                        index++;
                    }
                }

                return L;
            }
        }

        /// <summary>
        /// 观测量的权逆阵，一个对角阵。按照先伪距，后载波的顺序排列。
        /// 标准差由常数确定，载波比伪距标准差通常是 1:100，其方差比是 1:10000.
        /// 1.0/140*140=5.10e-5
        /// </summary> 
        /// <param name="factor">载波和伪距权逆阵因子（模糊度固定后，才采用默认值！）</param>
        /// <returns></returns>
        protected IMatrix BulidInverseWeightOfObs()
        {
            
            DiagonalMatrix inverseWeight = new DiagonalMatrix(ObsCount * 2);
            double invFactorOfRange = 1;
            double invFactorOfPhase = Option.PhaseCovaProportionToRange;
            
            int index = 0;
            foreach (var epoch in PreviousInfo)
            {
                foreach (var prn in epoch.EnabledPrns)
                {
                    EpochSatellite e = epoch[prn];
                    double inverseWeightOfSat = SatWeightProvider.GetInverseWeightOfRange(e);
                    
                    inverseWeight[index,index] = inverseWeightOfSat * invFactorOfRange;
                    inverseWeight[index + ObsCount,index + ObsCount] = inverseWeightOfSat * invFactorOfPhase;
                    index++;
                }
            }

            return  (inverseWeight);
        }

        #endregion

        #region 公共矩阵生成

        /// <summary>
        /// 参数平差系数阵 A。前一半行数为伪距观测量，后一半为载波观测量。构建为稀疏矩阵
        /// </summary>
        public override Matrix Coefficient
        {
            get
            {
                int rowCount = ObsCount * 2;
                int colCount = ParamCount;
                double[][] A = Geo.Utils.MatrixUtil.Create(rowCount, colCount);
                int row = 0;//行的索引号
                int i = 0;
                foreach (var item in PreviousInfo)
                { 
                    
                    var wetTropName = this.GnssParamNameBuilder.GetSiteWetTropZpdName(item);

                    foreach (var prn in item.EnabledSats)// 一颗卫星2行
                    {
                        double wetMap = item[prn.Prn].WetMap;
                        int next = row + ObsCount;
                        if (i != 0)
                        {
                            A[row][i] = 1.0;//接收机钟差对应的距离 = clkError * 光速
                            A[next][i] = 1.0;//接收机钟差对应的距离 = clkError * 光速
                        }

                        var satelliteName = this.GnssParamNameBuilder.GetSatClockParamName(prn.Prn);
                        var SiteSatAmbiguityName = this.GnssParamNameBuilder.GetSiteSatAmbiguityParamName(prn);
                        A[row][ParamNames.IndexOf(wetTropName)] = wetMap;
                        A[row][ParamNames.IndexOf(satelliteName)] = -1.0;//卫星钟差对应的距离 = clkError * 光速

                        A[next][ParamNames.IndexOf(wetTropName)] = wetMap;
                        A[next][ParamNames.IndexOf(satelliteName)] = -1.0;//卫星钟差对应的距离 = clkError * 光速
                        
                        row++;
                    }
                    i++;
                }
               // return new SparseMatrix(A);
                return new Matrix(A);
            }
        }


        #endregion

        public int SiteCount { get { return CurrentMaterial.Count; } }


    }
}