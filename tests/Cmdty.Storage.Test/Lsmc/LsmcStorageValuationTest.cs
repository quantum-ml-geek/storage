﻿#region License
// Copyright (c) 2020 Jake Fowler
//
// Permission is hereby granted, free of charge, to any person 
// obtaining a copy of this software and associated documentation 
// files (the "Software"), to deal in the Software without 
// restriction, including without limitation the rights to use, 
// copy, modify, merge, publish, distribute, sublicense, and/or sell 
// copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following 
// conditions:
//
// The above copyright notice and this permission notice shall be 
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, 
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES 
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND 
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT 
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, 
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR 
// OTHER DEALINGS IN THE SOFTWARE.
#endregion

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Cmdty.Core.Simulation.MultiFactor;
using Cmdty.TimePeriodValueTypes;
using Cmdty.TimeSeries;
using Xunit;
using Xunit.Abstractions;
using TimeSeriesFactory = Cmdty.TimeSeries.TimeSeries;

namespace Cmdty.Storage.Test
{
    // TODO test:
    // Call option test with two factors

    public sealed class LsmcStorageValuationTest
    {
        private const double SmallVol = 0.001;
        private const double TwoFactorCorr = 0.61;
        private const int NumSims = 2_000;
        private const double Inventory = 5_685;
        private const int RandomSeed = 11;
        private const int RegressMaxDegree = 2;
        private const double OneFactorMeanReversion = 12.5;
        private const double TrinomialTimeDelta = 1.0 / 365.0;
        private const int NumInventorySpacePoints = 100;
        private readonly TimeSeries<Day, double> _oneFactorFlatSpotVols;
        private readonly ITestOutputHelper _testOutputHelper;
        private readonly CmdtyStorage<Day> _simpleDailyStorage;
        private readonly CmdtyStorage<Day> _dailyStorageWithRatchets;
        private readonly CmdtyStorage<Day> _simpleDailyStorageTerminalInventoryValue;
        private readonly Func<double, double, double> _terminalInventoryValue;
        private readonly MultiFactorParameters<Day> _2FVeryLowVolDailyMultiFactorParams;
        private readonly List<BasisFunction> _oneFactorBasisFunctions;
        private readonly LsmcValuationParameters<Day>.Builder _1FactorParamsBuilder;
        private readonly LsmcValuationParameters<Day>.Builder _2FactorParamsBuilder;
        private readonly LsmcValuationParameters<Day>.Builder _triggerPriceTestParamsBuilder;

        public LsmcStorageValuationTest(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;

            #region Set Up Storage Objects
            var storageStart = new Day(2019, 8, 3);
            var storageEnd = new Day(2020, 4, 1);
            const double maxWithdrawalRate = 850.0;
            const double maxInjectionRate = 625.0;
            const double maxInventory = 52_500.0;
            const double constantInjectionCost = 1.25;
            const double constantWithdrawalCost = 0.93;

            _simpleDailyStorage = CmdtyStorage<Day>.Builder
                .WithActiveTimePeriod(storageStart, storageEnd)
                .WithConstantInjectWithdrawRange(-maxWithdrawalRate, maxInjectionRate)
                .WithZeroMinInventory()
                .WithConstantMaxInventory(maxInventory)
                .WithPerUnitInjectionCost(constantInjectionCost, injectionDate => injectionDate)
                .WithNoCmdtyConsumedOnInject()
                .WithPerUnitWithdrawalCost(constantWithdrawalCost, withdrawalDate => withdrawalDate)
                .WithNoCmdtyConsumedOnWithdraw()
                .WithNoCmdtyInventoryLoss()
                .WithNoInventoryCost()
                .MustBeEmptyAtEnd()
                .Build();

            _dailyStorageWithRatchets = CmdtyStorage<Day>.Builder
                .WithActiveTimePeriod(storageStart, storageEnd)
                .WithTimeAndInventoryVaryingInjectWithdrawRatesPiecewiseLinear(new List<InjectWithdrawRangeByInventoryAndPeriod<Day>>
                {
                    (period: storageStart, injectWithdrawRanges: new List<InjectWithdrawRangeByInventory>
                    {
                        (inventory: 0.0, (minInjectWithdrawRate: -702.7, maxInjectWithdrawRate: 650.0)),
                        (inventory: 15_000, (minInjectWithdrawRate: -785.0, maxInjectWithdrawRate: 552.5)),
                        (inventory: 30_000, (minInjectWithdrawRate: -790.6, maxInjectWithdrawRate: 512.8)),
                        (inventory: 40_000, (minInjectWithdrawRate: -825.6, maxInjectWithdrawRate: 498.6)),
                        (inventory: 52_500, (minInjectWithdrawRate: -850.4, maxInjectWithdrawRate: 480.0)),
                    }),
                    (period: new Day(2020, 2, 1), injectWithdrawRanges: new List<InjectWithdrawRangeByInventory>
                    {
                        (inventory: 0.0, (minInjectWithdrawRate: -645.35, maxInjectWithdrawRate: 650.0)),
                        (inventory: 13_000, (minInjectWithdrawRate: -656.0, maxInjectWithdrawRate: 552.5)),
                        (inventory: 28_000, (minInjectWithdrawRate: -689.6, maxInjectWithdrawRate: 512.8)),
                        (inventory: 42_000, (minInjectWithdrawRate: -701.06, maxInjectWithdrawRate: 498.6)),
                        (inventory: 52_500, (minInjectWithdrawRate: -718.04, maxInjectWithdrawRate: 480.0)),
                    }),
                })
                .WithPerUnitInjectionCost(constantInjectionCost, injectionDate => injectionDate)
                .WithNoCmdtyConsumedOnInject()
                .WithPerUnitWithdrawalCost(constantWithdrawalCost, withdrawalDate => withdrawalDate)
                .WithNoCmdtyConsumedOnWithdraw()
                .WithNoCmdtyInventoryLoss()
                .WithNoInventoryCost()
                .MustBeEmptyAtEnd()
                .Build();

            _terminalInventoryValue = (cmdtyPrice, terminalInventory) => cmdtyPrice * terminalInventory - 999.0;
            _simpleDailyStorageTerminalInventoryValue = CmdtyStorage<Day>.Builder
                .WithActiveTimePeriod(storageStart, storageEnd)
                .WithConstantInjectWithdrawRange(-maxWithdrawalRate, maxInjectionRate)
                .WithZeroMinInventory()
                .WithConstantMaxInventory(maxInventory)
                .WithPerUnitInjectionCost(constantInjectionCost, injectionDate => injectionDate)
                .WithNoCmdtyConsumedOnInject()
                .WithPerUnitWithdrawalCost(constantWithdrawalCost, withdrawalDate => withdrawalDate)
                .WithNoCmdtyConsumedOnWithdraw()
                .WithNoCmdtyInventoryLoss()
                .WithNoInventoryCost()
                .WithTerminalInventoryNpv(_terminalInventoryValue)
                .Build();
            #endregion Set Up Storage Objects

            var valDate = new Day(2019, 8, 29);
            const double oneFactorSpotVol = 0.95;
            _oneFactorFlatSpotVols = TimeSeriesFactory.ForConstantData(valDate, storageEnd, oneFactorSpotVol);
            var oneFactorDailyMultiFactorParams = MultiFactorParameters.For1Factor(OneFactorMeanReversion, _oneFactorFlatSpotVols);
            var smallFlatSpotVols = TimeSeriesFactory.ForConstantData(valDate, storageEnd, SmallVol);
            _2FVeryLowVolDailyMultiFactorParams = MultiFactorParameters.For2Factors(TwoFactorCorr,
                new Factor<Day>(0.0, smallFlatSpotVols),
                new Factor<Day>(OneFactorMeanReversion, smallFlatSpotVols));

            const double flatInterestRate = 0.055;
            var gridCalc = FixedSpacingStateSpaceGridCalc.CreateForFixedNumberOfPointsOnGlobalInventoryRange(_simpleDailyStorage, NumInventorySpacePoints);
            var discounter = StorageHelper.CreateAct65ContCompDiscounter(flatInterestRate);

            Day SettleDateRule(Day deliveryDate) => Month.FromDateTime(deliveryDate.Start).Offset(1).First<Day>() + 19;

            const double baseForwardPrice = 53.5;
            const double forwardSeasonalFactor = 24.6;
            var forwardCurve = TimeSeriesFactory.FromMap(valDate, storageEnd, day =>
            {
                int daysForward = day.OffsetFrom(valDate);
                return baseForwardPrice + Math.Sin(2.0 * Math.PI / 365.0 * daysForward) * forwardSeasonalFactor;
            });

            _oneFactorBasisFunctions = BasisFunctionsBuilder.Ones + 
                                       BasisFunctionsBuilder.AllMarkovFactorAllPositiveIntegerPowersUpTo(RegressMaxDegree, 1);

            _1FactorParamsBuilder = new LsmcValuationParameters<Day>.Builder
            {
                CurrentPeriod = valDate,
                Inventory = Inventory,
                ForwardCurve = forwardCurve,
                SettleDateRule = SettleDateRule,
                DiscountFactors = discounter,
                GridCalc = gridCalc,
                BasisFunctions = _oneFactorBasisFunctions,
            }
            .SimulateWithMultiFactorModelAndMersenneTwister(oneFactorDailyMultiFactorParams, NumSims, RandomSeed, RandomSeed * 2);

            const int triggerPriceTestNumSims = 500;
            _triggerPriceTestParamsBuilder = new LsmcValuationParameters<Day>.Builder
                {
                    CurrentPeriod = valDate,
                    Inventory = Inventory,
                    ForwardCurve = forwardCurve,
                    SettleDateRule = SettleDateRule,
                    DiscountFactors = discounter,
                    GridCalc = gridCalc,
                    BasisFunctions = _oneFactorBasisFunctions,
                    Storage = _simpleDailyStorage
                }
                .SimulateWithMultiFactorModelAndMersenneTwister(oneFactorDailyMultiFactorParams, triggerPriceTestNumSims, RandomSeed);

            _2FactorParamsBuilder = new LsmcValuationParameters<Day>.Builder
                {
                    CurrentPeriod = valDate,
                    Inventory = Inventory,
                    ForwardCurve = forwardCurve,
                    SettleDateRule = SettleDateRule,
                    DiscountFactors = StorageHelper.CreateAct65ContCompDiscounter(flatInterestRate),
                    GridCalc = FixedSpacingStateSpaceGridCalc.CreateForFixedNumberOfPointsOnGlobalInventoryRange(_simpleDailyStorage, NumInventorySpacePoints),
                    BasisFunctions = BasisFunctionsBuilder.Ones +
                                     BasisFunctionsBuilder.AllMarkovFactorAllPositiveIntegerPowersUpTo(RegressMaxDegree, 2)
            };
        }

        [Fact]
        [Trait("Category", "Lsmc.AtEndOfStorage")]
        public void Calculate_CurrentPeriodAfterStorageEnd_ResultWithZeroNpv()
        {
            var builder = _1FactorParamsBuilder.Clone();
            builder.CurrentPeriod = _simpleDailyStorage.EndPeriod + 1;
            builder.Storage = _simpleDailyStorage;
            LsmcStorageValuationResults<Day> lsmcResults = LsmcStorageValuation.WithNoLogger.Calculate(builder.Build());
            Assert.Equal(0.0, lsmcResults.Npv);
        }

        [Fact]
        [Trait("Category", "Lsmc.AtEndOfStorage")]
        public void Calculate_CurrentPeriodAfterStorageEnd_ResultWithEmptyDeltas()
        {
            var builder = _1FactorParamsBuilder.Clone();
            builder.CurrentPeriod = _simpleDailyStorage.EndPeriod + 1;
            builder.Storage = _simpleDailyStorage;
            LsmcStorageValuationResults<Day> lsmcResults = LsmcStorageValuation.WithNoLogger.Calculate(builder.Build());
            Assert.True(lsmcResults.Deltas.IsEmpty);
        }
        // TODO same unit test as above, but testing the other output data, decision, simulated prices etc.

        [Fact]
        [Trait("Category", "Lsmc.AtEndOfStorage")]
        public void Calculate_CurrentPeriodEqualToStorageEndStorageMustBeEmptyAtEnd_ResultWithZeroNpv()
        {
            var builder = _1FactorParamsBuilder.Clone();
            builder.CurrentPeriod = _simpleDailyStorage.EndPeriod;
            builder.Inventory = 0.0;
            builder.Storage = _simpleDailyStorage;
            LsmcStorageValuationResults<Day> lsmcResults = LsmcStorageValuation.WithNoLogger.Calculate(builder.Build());
            Assert.Equal(0.0, lsmcResults.Npv);
        }

        [Fact]
        [Trait("Category", "Lsmc.AtEndOfStorage")]
        public void Calculate_CurrentPeriodEqualToStorageEndStorageMustBeEmptyAtEnd_ResultWithEmptyDeltas()
        {
            var builder = _1FactorParamsBuilder.Clone();
            builder.CurrentPeriod = _simpleDailyStorage.EndPeriod;
            builder.Inventory = 0.0;
            builder.Storage = _simpleDailyStorage;
            LsmcStorageValuationResults<Day> lsmcResults = LsmcStorageValuation.WithNoLogger.Calculate(builder.Build());
            Assert.True(lsmcResults.Deltas.IsEmpty);
        }

        // TODO same unit test as above, but testing the other output data, decision, simulated prices etc.

        [Fact]
        [Trait("Category", "Lsmc.AtEndOfStorage")]
        public void Calculate_CurrentPeriodEqualToStorageEndAndInventoryHasTerminalValue_NpvEqualsTerminalValue()
        {
            var builder = _1FactorParamsBuilder.Clone();
            builder.CurrentPeriod = _simpleDailyStorageTerminalInventoryValue.EndPeriod;
            builder.Inventory = 0.0;
            builder.Storage = _simpleDailyStorageTerminalInventoryValue;
            var lsmcParams = builder.Build();
            LsmcStorageValuationResults<Day> lsmcResults = LsmcStorageValuation.WithNoLogger.Calculate(lsmcParams);

            double valDateSpotPrice = lsmcParams.ForwardCurve[builder.CurrentPeriod];
            double expectedNpv = _terminalInventoryValue(valDateSpotPrice, lsmcParams.Inventory);
            Assert.Equal(expectedNpv, lsmcResults.Npv);
        }

        // TODO same unit test as above, but testing the other output data, delta, decision, simulated prices etc.

        [Fact]
        [Trait("Category", "Lsmc.AtEndOfStorage")]
        public void Calculate_CurrentPeriodDayBeforeStorageEndAndStorageMustBeEmptyAtEnd_NpvEqualsInventoryTimesSpotMinusWithdrawalCost()
        {
            var builder = _1FactorParamsBuilder.Clone();
            builder.CurrentPeriod = _simpleDailyStorage.EndPeriod - 1;
            builder.Inventory = 352.14;
            builder.Storage = _simpleDailyStorage;
            var lsmcParams = builder.Build();
            LsmcStorageValuationResults<Day> lsmcResults = LsmcStorageValuation.WithNoLogger.Calculate(lsmcParams);
            const double constantWithdrawalCost = 0.93;

            double valDateSpotPrice = lsmcParams.ForwardCurve[lsmcParams.CurrentPeriod];
            double discountFactor = lsmcParams.DiscountFactors(lsmcParams.CurrentPeriod, lsmcParams.SettleDateRule(lsmcParams.CurrentPeriod));
            double expectedNpv = lsmcParams.Inventory * valDateSpotPrice * discountFactor - constantWithdrawalCost * lsmcParams.Inventory;
            Assert.Equal(expectedNpv, lsmcResults.Npv, 8);
        }

        [Fact]
        [Trait("Category", "Lsmc.AtEndOfStorage")]
        public void Calculate_CurrentPeriodDayBeforeStorageEndAndStorageMustBeEmptyAtEnd_DeltaEqualsInventory()
        {
            var builder = _1FactorParamsBuilder.Clone();
            builder.CurrentPeriod = _simpleDailyStorage.EndPeriod - 1;
            builder.Storage = _simpleDailyStorage;
            builder.Inventory = 352.14;
            var lsmcParams = builder.Build();
            LsmcStorageValuationResults<Day> lsmcResults = LsmcStorageValuation.WithNoLogger.Calculate(lsmcParams);

            Assert.Equal(lsmcParams.Inventory, lsmcResults.Deltas[lsmcParams.CurrentPeriod], 10);
        }

        // TODO same unit test as above, but testing the other output data, decision, simulated prices etc.

        // TODO terminal value looks like call option payoff, value day before equals call value

        [Fact]
        [Trait("Category", "Lsmc.LikeCalls")]
        public void Calculate_StorageLikeCallOptionsOneFactor_NpvEqualsBlack76()
        {
            var paramsBuilder = new LsmcValuationParameters<Day>.Builder
            {
                CurrentPeriod = new Day(2019, 8, 29),
                BasisFunctions = _1FactorParamsBuilder.BasisFunctions
            };
            const int numInventorySpacePoints = 100;
            const int numSims = 2_000;
            const int seed = 11;
            const int forwardSeed = 13;

            (DoubleTimeSeries<Day> forwardCurve, DoubleTimeSeries<Day> spotVolCurve) =
                TestHelper.CreateDailyTestForwardAndSpotVolCurves(paramsBuilder.CurrentPeriod, new Day(2020, 4, 1));
            paramsBuilder.ForwardCurve = forwardCurve;
            const double meanReversion = 16.5;
            const double interestRate = 0.09;

            TestHelper.CallOptionLikeTestData testData = TestHelper.CreateThreeCallsLikeStorageTestData(forwardCurve);
            paramsBuilder.Inventory = testData.Inventory;
            paramsBuilder.Storage = testData.Storage;

            var multiFactorParams = MultiFactorParameters.For1Factor(meanReversion, spotVolCurve);

            paramsBuilder.SimulateWithMultiFactorModelAndMersenneTwister(multiFactorParams, numSims, seed, forwardSeed);

            paramsBuilder.SettleDateRule = settleDate => testData.SettleDates[Month.FromDateTime(settleDate.Start)];
            paramsBuilder.DiscountFactors = StorageHelper.CreateAct65ContCompDiscounter(interestRate);
            paramsBuilder.GridCalc =
                FixedSpacingStateSpaceGridCalc.CreateForFixedNumberOfPointsOnGlobalInventoryRange(testData.Storage, numInventorySpacePoints);

            var lsmcParams = paramsBuilder.Build();
            LsmcStorageValuationResults<Day> results = LsmcStorageValuation.WithNoLogger.Calculate(lsmcParams);

            // Calculate value of equivalent call options
            double expectStorageValue = 0.0;
            foreach (TestHelper.CallOption option in testData.CallOptions)
            {
                double impliedVol = TestHelper.OneFactorImpliedVol(lsmcParams.CurrentPeriod, option.ExpiryDate, spotVolCurve, meanReversion);
                double forwardPrice = forwardCurve[option.ExpiryDate];
                double black76Value = TestHelper.Black76CallOptionValue(lsmcParams.CurrentPeriod, forwardPrice,
                                          impliedVol, interestRate, option.StrikePrice, option.ExpiryDate,
                                          option.SettleDate) * option.NotionalVolume;
                expectStorageValue += black76Value;
            }

            double percentError = (results.Npv - expectStorageValue) / expectStorageValue;
            const double percentErrorLowerBound = -0.02; // Calculated value cannot be more than 2% lower than call options
            const double percentErrorUpperBound = 0.0; // Calculate value will not be higher than call options as LSMC is a lower bound approximation

            _testOutputHelper.WriteLine(percentError.ToString("P5"));
            Assert.InRange(percentError, percentErrorLowerBound, percentErrorUpperBound);
        }

        [Fact]
        [Trait("Category", "Lsmc.LikeCalls")]
        public void Calculate_StorageLikeCallOptionsOneFactor_DeltaEqualsBlack76DeltaUndiscounted()
        {
            var paramsBuilder = new LsmcValuationParameters<Day>.Builder
            {
                CurrentPeriod = new Day(2019, 8, 29),
                BasisFunctions = _1FactorParamsBuilder.BasisFunctions
            };
            const int numInventorySpacePoints = 100;
            const int numSims = 2_000;
            const int seed = 11;
            const int forwardSeed = 14;

            (DoubleTimeSeries<Day> forwardCurve, DoubleTimeSeries<Day> spotVolCurve) =
                TestHelper.CreateDailyTestForwardAndSpotVolCurves(paramsBuilder.CurrentPeriod, new Day(2020, 4, 1));
            paramsBuilder.ForwardCurve = forwardCurve;
            const double meanReversion = 16.5;
            const double interestRate = 0.09;

            TestHelper.CallOptionLikeTestData testData = TestHelper.CreateThreeCallsLikeStorageTestData(forwardCurve);
            paramsBuilder.Inventory = testData.Inventory;
            paramsBuilder.Storage = testData.Storage;

            var multiFactorParams = MultiFactorParameters.For1Factor(meanReversion, spotVolCurve);

            paramsBuilder.SimulateWithMultiFactorModelAndMersenneTwister(multiFactorParams, numSims, seed,  forwardSeed);

            paramsBuilder.SettleDateRule = settleDate => testData.SettleDates[Month.FromDateTime(settleDate.Start)];
            paramsBuilder.DiscountFactors = StorageHelper.CreateAct65ContCompDiscounter(interestRate);
            paramsBuilder.GridCalc =
                FixedSpacingStateSpaceGridCalc.CreateForFixedNumberOfPointsOnGlobalInventoryRange(testData.Storage, numInventorySpacePoints);

            var lsmcParams = paramsBuilder.Build();
            LsmcStorageValuationResults<Day> results = LsmcStorageValuation.WithNoLogger.Calculate(lsmcParams);

            const double tol = 0.02; // 2% tolerance

            foreach ((Day day, double delta) in results.Deltas)
            {
                if (testData.CallOptions.Any(option => option.ExpiryDate == day))
                {
                    TestHelper.CallOption option = testData.CallOptions.Single(call => call.ExpiryDate == day);
                    double impliedVol =
                        TestHelper.OneFactorImpliedVol(lsmcParams.CurrentPeriod, option.ExpiryDate, spotVolCurve, meanReversion);
                    double forwardPrice = forwardCurve[option.ExpiryDate];
                    double black76DeltaUndiscounted = TestHelper.Black76CallOptionDeltaUndiscounted(lsmcParams.CurrentPeriod, forwardPrice,
                                                          impliedVol, option.StrikePrice, option.ExpiryDate) * option.NotionalVolume;
                    double storageDelta = results.Deltas[option.ExpiryDate];
                    // Storage delta should 
                    TestHelper.AssertWithinPercentTol(storageDelta, black76DeltaUndiscounted, tol);
                }
                else
                    Assert.Equal(0.0, delta);
            }
        }

        [Fact]
        [Trait("Category", "Lsmc.LikeTrinomial")]
        public void Calculate_OneFactorSimpleStorage_NpvApproximatelyEqualsTrinomialNpv()
        {
            const int trinomialNumInventorySpacePoints = 500; // Finer grid for trinomial as this model misses out on some convexity without a forward simulation.
            var builder = _1FactorParamsBuilder.Clone();
            builder.Storage = _simpleDailyStorage;
            var lsmcParams = builder.Build();
            LsmcStorageValuationResults<Day> lsmcResults = LsmcStorageValuation.WithNoLogger.Calculate(lsmcParams);

            TreeStorageValuationResults<Day> treeResults = TreeStorageValuation<Day>.ForStorage(lsmcParams.Storage)
                .WithStartingInventory(Inventory)
                .ForCurrentPeriod(lsmcParams.CurrentPeriod)
                .WithForwardCurve(lsmcParams.ForwardCurve)
                .WithOneFactorTrinomialTree(_oneFactorFlatSpotVols, OneFactorMeanReversion, TrinomialTimeDelta)
                .WithCmdtySettlementRule(lsmcParams.SettleDateRule)
                .WithDiscountFactorFunc(lsmcParams.DiscountFactors)
                .WithFixedNumberOfPointsOnGlobalInventoryRange(trinomialNumInventorySpacePoints)
                .WithLinearInventorySpaceInterpolation()
                .WithNumericalTolerance(lsmcParams.NumericalTolerance)
                .Calculate();

            _testOutputHelper.WriteLine("Tree");
            _testOutputHelper.WriteLine(treeResults.NetPresentValue.ToString(CultureInfo.InvariantCulture));
            _testOutputHelper.WriteLine("LSMC");
            _testOutputHelper.WriteLine(lsmcResults.Npv.ToString(CultureInfo.InvariantCulture));
            const double percentageTol = 0.005; // 0.5%
            TestHelper.AssertWithinPercentTol(treeResults.NetPresentValue, lsmcResults.Npv, percentageTol);
        }

        [Fact]
        [Trait("Category", "Lsmc.LikeTrinomial")]
        public void Calculate_OneFactorStorageWithRatchets_NpvApproximatelyEqualsTrinomialNpv()
        {
            const int trinomialNumInventorySpacePoints = 500; // Finer grid for trinomial as this model misses out on some convexity without a forward simulation.
            var builder = _1FactorParamsBuilder.Clone();
            builder.Storage = _dailyStorageWithRatchets;
            var lsmcParams = builder.Build();
            LsmcStorageValuationResults<Day> lsmcResults = LsmcStorageValuation.WithNoLogger.Calculate(lsmcParams);

            TreeStorageValuationResults<Day> treeResults = TreeStorageValuation<Day>.ForStorage(lsmcParams.Storage)
                .WithStartingInventory(Inventory)
                .ForCurrentPeriod(lsmcParams.CurrentPeriod)
                .WithForwardCurve(lsmcParams.ForwardCurve)
                .WithOneFactorTrinomialTree(_oneFactorFlatSpotVols, OneFactorMeanReversion, TrinomialTimeDelta)
                .WithCmdtySettlementRule(lsmcParams.SettleDateRule)
                .WithDiscountFactorFunc(lsmcParams.DiscountFactors)
                .WithFixedNumberOfPointsOnGlobalInventoryRange(trinomialNumInventorySpacePoints)
                .WithLinearInventorySpaceInterpolation()
                .WithNumericalTolerance(lsmcParams.NumericalTolerance)
                .Calculate();

            _testOutputHelper.WriteLine("Tree");
            _testOutputHelper.WriteLine(treeResults.NetPresentValue.ToString(CultureInfo.InvariantCulture));
            _testOutputHelper.WriteLine("LSMC");
            _testOutputHelper.WriteLine(lsmcResults.Npv.ToString(CultureInfo.InvariantCulture));
            const double percentageTol = 0.006; // 0.6%
            TestHelper.AssertWithinPercentTol(treeResults.NetPresentValue, lsmcResults.Npv, percentageTol);
        }

        [Fact(Skip = "Still working on this.")]
        [Trait("Category", "Lsmc.LikeTrinomial")]
        //[Fact]
        public void Calculate_OneFactorSimpleStorageValDateAfterStorageStart_NpvApproximatelyEqualsTrinomialNpv()
        {
            var builder = _1FactorParamsBuilder.Clone();
            builder.CurrentPeriod = _simpleDailyStorage.StartPeriod + 10;
            builder.Storage = _simpleDailyStorage;
            LsmcValuationParameters<Day> lsmcParams = builder.Build();
            LsmcStorageValuationResults<Day> lsmcResults = LsmcStorageValuation.WithNoLogger.Calculate(lsmcParams);

            TreeStorageValuationResults<Day> treeResults = TreeStorageValuation<Day>.ForStorage(lsmcParams.Storage)
                .WithStartingInventory(Inventory)
                .ForCurrentPeriod(lsmcParams.CurrentPeriod)
                .WithForwardCurve(lsmcParams.ForwardCurve)
                .WithOneFactorTrinomialTree(_oneFactorFlatSpotVols, OneFactorMeanReversion, TrinomialTimeDelta)
                .WithCmdtySettlementRule(lsmcParams.SettleDateRule)
                .WithDiscountFactorFunc(lsmcParams.DiscountFactors)
                .WithFixedNumberOfPointsOnGlobalInventoryRange(NumInventorySpacePoints)
                .WithLinearInventorySpaceInterpolation()
                .WithNumericalTolerance(lsmcParams.NumericalTolerance)
                .Calculate();

            _testOutputHelper.WriteLine("Tree");
            _testOutputHelper.WriteLine(treeResults.NetPresentValue.ToString(CultureInfo.InvariantCulture));
            _testOutputHelper.WriteLine("LSMC");
            _testOutputHelper.WriteLine(lsmcResults.Npv.ToString(CultureInfo.InvariantCulture));
            const double percentageTol = 0.005; // 0.5%
            TestHelper.AssertWithinPercentTol(treeResults.NetPresentValue, lsmcResults.Npv, percentageTol);
        }

        private IntrinsicStorageValuationResults<Day> CalcIntrinsic(LsmcValuationParameters<Day> lsmcParams) => IntrinsicStorageValuation<Day>
                                            .ForStorage(lsmcParams.Storage)
                                            .WithStartingInventory(Inventory)
                                            .ForCurrentPeriod(lsmcParams.CurrentPeriod)
                                            .WithForwardCurve(lsmcParams.ForwardCurve)
                                            .WithCmdtySettlementRule(lsmcParams.SettleDateRule)
                                            .WithDiscountFactorFunc(lsmcParams.DiscountFactors)
                                            .WithFixedNumberOfPointsOnGlobalInventoryRange(NumInventorySpacePoints)
                                            .WithLinearInventorySpaceInterpolation()
                                            .WithNumericalTolerance(lsmcParams.NumericalTolerance)
                                            .Calculate();


        // TODO investigate why the below two test require a high tolerance. I suspect this is due to a high 'foresight' bias caused by using the same simulation for regression and decision
        [Fact]
        [Trait("Category", "Lsmc.LikeIntrinsic")]
        public void Calculate_OneFactorZeroMeanReversionSimpleStorage_NpvApproximatelyEqualsIntrinsicNpv()
        {
            const int regressPolyDegree = 5;  // Test requires a higher poly degree than the others
            var builder = _1FactorParamsBuilder.Clone();
            builder.Storage = _simpleDailyStorage;
            builder.SimulateWithMultiFactorModelAndMersenneTwister(MultiFactorParameters.For1Factor(0.0, _oneFactorFlatSpotVols), NumSims, RandomSeed);
            builder.BasisFunctions = BasisFunctionsBuilder.Ones +
                                     BasisFunctionsBuilder.AllMarkovFactorAllPositiveIntegerPowersUpTo(regressPolyDegree, 1);

            LsmcValuationParameters<Day> lsmcParams = builder.Build();
            LsmcStorageValuationResults<Day> lsmcResults = LsmcStorageValuation.WithNoLogger.Calculate(lsmcParams);
            IntrinsicStorageValuationResults<Day> intrinsicResults = CalcIntrinsic(lsmcParams);

            const double percentageTol = 0.04; // 4%
            _testOutputHelper.WriteLine(intrinsicResults.Npv.ToString(CultureInfo.InvariantCulture));
            _testOutputHelper.WriteLine(lsmcResults.Npv.ToString(CultureInfo.InvariantCulture));
            TestHelper.AssertWithinPercentTol(intrinsicResults.Npv, lsmcResults.Npv, percentageTol);
        }

        [Fact]
        [Trait("Category", "Lsmc.LikeIntrinsic")]
        public void Calculate_OneFactorZeroMeanReversionStorageWithRatchets_NpvApproximatelyEqualsIntrinsicNpv()
        {
            const int regressPolyDegree = 5;  // Test requires a higher poly degree than the others
            var builder = _1FactorParamsBuilder.Clone();
            builder.Storage = _dailyStorageWithRatchets;
            builder.SimulateWithMultiFactorModelAndMersenneTwister(MultiFactorParameters.For1Factor(0.0, _oneFactorFlatSpotVols), NumSims, RandomSeed);
            builder.BasisFunctions = BasisFunctionsBuilder.Ones +
                                     BasisFunctionsBuilder.AllMarkovFactorAllPositiveIntegerPowersUpTo(regressPolyDegree, 1);

            LsmcValuationParameters<Day> lsmcParams = builder.Build();
            LsmcStorageValuationResults<Day> lsmcResults = LsmcStorageValuation.WithNoLogger.Calculate(lsmcParams);
            IntrinsicStorageValuationResults<Day> intrinsicResults = CalcIntrinsic(lsmcParams);

            const double percentageTol = 0.04; // 4%
            _testOutputHelper.WriteLine(intrinsicResults.Npv.ToString(CultureInfo.InvariantCulture));
            _testOutputHelper.WriteLine(lsmcResults.Npv.ToString(CultureInfo.InvariantCulture));
            TestHelper.AssertWithinPercentTol(intrinsicResults.Npv, lsmcResults.Npv, percentageTol);
        }

        [Fact]
        [Trait("Category", "Lsmc.LikeIntrinsic")]
        public void Calculate_OneFactorVeryLowVolsSimpleStorage_NpvApproximatelyEqualsIntrinsicNpv()
        {
            var builder = _1FactorParamsBuilder.Clone();
            builder.Storage = _simpleDailyStorage;

            var smallFlatSpotVols = TimeSeriesFactory.ForConstantData(builder.CurrentPeriod, builder.Storage.EndPeriod, SmallVol);
            var oneFactorVeryLowVolDailyMultiFactorParams =
                MultiFactorParameters.For1Factor(OneFactorMeanReversion, smallFlatSpotVols);
            builder.SimulateWithMultiFactorModelAndMersenneTwister(oneFactorVeryLowVolDailyMultiFactorParams, NumSims, RandomSeed);

            LsmcValuationParameters<Day> lsmcParams = builder.Build();
            LsmcStorageValuationResults<Day> lsmcResults = LsmcStorageValuation.WithNoLogger.Calculate(lsmcParams);
            IntrinsicStorageValuationResults<Day> intrinsicResults = CalcIntrinsic(lsmcParams);

            const double percentageTol = 0.0001; // 0.01%
            TestHelper.AssertWithinPercentTol(intrinsicResults.Npv, lsmcResults.Npv, percentageTol);
        }

        [Fact]
        [Trait("Category", "Lsmc.LikeIntrinsic")]
        public void Calculate_OneFactorVeryLowVolsStorageWithRatchets_NpvApproximatelyEqualsIntrinsicNpv()
        {
            var builder = _1FactorParamsBuilder.Clone();
            builder.Storage = _dailyStorageWithRatchets;

            var smallFlatSpotVols = TimeSeriesFactory.ForConstantData(builder.CurrentPeriod, builder.Storage.EndPeriod, SmallVol);
            var oneFactorVeryLowVolDailyMultiFactorParams =
                MultiFactorParameters.For1Factor(OneFactorMeanReversion, smallFlatSpotVols);
            builder.SimulateWithMultiFactorModelAndMersenneTwister(oneFactorVeryLowVolDailyMultiFactorParams, NumSims, RandomSeed);

            LsmcValuationParameters<Day> lsmcParams = builder.Build();
            LsmcStorageValuationResults<Day> lsmcResults = LsmcStorageValuation.WithNoLogger.Calculate(lsmcParams);
            IntrinsicStorageValuationResults<Day> intrinsicResults = CalcIntrinsic(lsmcParams);

            const double percentageTol = 0.0004; // 0.04%
            TestHelper.AssertWithinPercentTol(intrinsicResults.Npv, lsmcResults.Npv, percentageTol);
        }

        [Fact]
        [Trait("Category", "Lsmc.LikeIntrinsic")]
        public void Calculate_OneFactorVeryLowVolsSimpleStorage_DeltasApproximatelyEqualIntrinsicVolumeProfile()
        {
            Assert_VeryLowVol_DeltasEqualIntrinsicVolumeProfile(_simpleDailyStorage);
        }

        [Fact(Skip = "Need to investigate why this is failing.")] // TODO investigate
        [Trait("Category", "Lsmc.LikeIntrinsic")]
        public void Calculate_OneFactorVeryLowVolsStorageWithRatchets_DeltasApproximatelyEqualIntrinsicVolumeProfile()
        {
            Assert_VeryLowVol_DeltasEqualIntrinsicVolumeProfile(_dailyStorageWithRatchets);
        }

        private void Assert_VeryLowVol_DeltasEqualIntrinsicVolumeProfile(CmdtyStorage<Day> storage)
        {
            var builder = _1FactorParamsBuilder.Clone();
            builder.Storage = storage;
            var smallFlatSpotVols = TimeSeriesFactory.ForConstantData(builder.CurrentPeriod, builder.Storage.EndPeriod, SmallVol);
            var oneFactorVeryLowVolDailyMultiFactorParams =
                MultiFactorParameters.For1Factor(OneFactorMeanReversion, smallFlatSpotVols);
            builder.SimulateWithMultiFactorModelAndMersenneTwister(oneFactorVeryLowVolDailyMultiFactorParams, NumSims, RandomSeed);

            LsmcValuationParameters<Day> lsmcParams = builder.Build();

            LsmcStorageValuationResults<Day> lsmcResults = LsmcStorageValuation.WithNoLogger.Calculate(lsmcParams);
            IntrinsicStorageValuationResults<Day> intrinsicResults = CalcIntrinsic(lsmcParams);

            TimeSeries<Day, StorageProfile> intrinsicProfile = intrinsicResults.StorageProfile;
            DoubleTimeSeries<Day> lsmcDeltas = lsmcResults.Deltas;

            Assert.Equal(intrinsicProfile.Start, lsmcDeltas.Start);
            Assert.Equal(intrinsicProfile.End, lsmcDeltas.End);

            const int precision = 5;

            foreach (Day day in intrinsicProfile.Indices)
            {
                double intrinsicVolume = intrinsicProfile[day].NetVolume;
                double lsmcDelta = lsmcDeltas[day];
                Assert.Equal(intrinsicVolume, lsmcDelta, precision);
            }
        }

        [Fact]
        [Trait("Category", "Lsmc.LikeIntrinsic")]
        public void Calculate_TwoFactorVeryLowVolsSimpleStorage_NpvApproximatelyEqualsIntrinsicNpv()
        {
            var builder = _2FactorParamsBuilder.Clone();
            builder.Storage = _simpleDailyStorage;
            builder.SimulateWithMultiFactorModelAndMersenneTwister(_2FVeryLowVolDailyMultiFactorParams, NumSims, RandomSeed);
            var lsmcParams = builder.Build();

            LsmcStorageValuationResults<Day> lsmcResults = LsmcStorageValuation.WithNoLogger.Calculate(lsmcParams);
            IntrinsicStorageValuationResults<Day> intrinsicResults = CalcIntrinsic(lsmcParams);

            const double percentageTol = 0.0001; // 0.01%
            TestHelper.AssertWithinPercentTol(intrinsicResults.Npv, lsmcResults.Npv, percentageTol);
        }

        [Fact]
        [Trait("Category", "Lsmc.LikeIntrinsic")]
        public void Calculate_TwoFactorVeryLowVolsStorageWithRatchets_NpvApproximatelyEqualsIntrinsicNpv()
        {
            var builder = _2FactorParamsBuilder.Clone();
            builder.Storage = _dailyStorageWithRatchets;
            builder.SimulateWithMultiFactorModelAndMersenneTwister(_2FVeryLowVolDailyMultiFactorParams, NumSims, RandomSeed);
            var lsmcParams = builder.Build();

            LsmcStorageValuationResults<Day> lsmcResults = LsmcStorageValuation.WithNoLogger.Calculate(lsmcParams);
            IntrinsicStorageValuationResults<Day> intrinsicResults = CalcIntrinsic(lsmcParams);

            const double percentageTol = 0.0004; // 0.01%
            TestHelper.AssertWithinPercentTol(intrinsicResults.Npv, lsmcResults.Npv, percentageTol);
        }

        // TODO refactor this to share code with trinomial test
        [Fact]
        [Trait("Category", "Lsmc.LikeIntrinsic")]
        public void Calculate_StorageWithForcedInjectAndWithdraw_NpvAlmostEqualsTrivialIntrinsicCalc()
        {
            // There will be a small difference between the LSMC NPV and the intrinsic calc because of sampling error
            // between the simulated spot price and the forward price. Thi isn't the case with the trinomial tree as by construction
            // the tree expected spot price will exactly equal the forward price
            var currentDate = new Day(2019, 8, 29);
            const double percentageTol = 0.03;

            var storageStart = new Day(2019, 12, 1);
            var storageEnd = new Day(2020, 4, 1);

            const double minInventory = 0.0;
            const double maxInventory = 10_000.0;

            const double forcedInjectionRate = 211.5;
            const int forcedInjectionNumDays = 20;
            var forcedInjectionStart = new Day(2019, 12, 20);

            const double injectionPerUnitCost = 1.23;
            const double injectionCmdtyConsumed = 0.01;

            const double forcedWithdrawalRate = 187.54;
            const int forcedWithdrawalNumDays = 15;
            var forcedWithdrawalStart = new Day(2020, 2, 5);

            const double withdrawalPerUnitCost = 0.98;
            const double withdrawalCmdtyConsumed = 0.015;

            (DoubleTimeSeries<Day> forwardCurve, DoubleTimeSeries<Day> spotVolCurve) = TestHelper.CreateDailyTestForwardAndSpotVolCurves(currentDate, storageEnd);
            const double meanReversion = 16.5;
            const double interestRate = 0.09;

            TimeSeries<Month, Day> settlementDates = new TimeSeries<Month, Day>.Builder()
            {
                { new Month(2019, 12),  new Day(2020, 1, 20)},
                { new Month(2020, 1),  new Day(2020, 2, 18)},
                { new Month(2020, 2),  new Day(2020, 3, 21)},
                { new Month(2020, 3),  new Day(2020, 4, 22)}
            }.Build();

            var injectWithdrawConstraints = new List<InjectWithdrawRangeByInventoryAndPeriod<Day>>
            {
                (period: storageStart, injectWithdrawRanges: new List<InjectWithdrawRangeByInventory>
                {
                    (inventory: minInventory, (minInjectWithdrawRate: 0.0, maxInjectWithdrawRate: 0.0)),
                    (inventory: maxInventory, (minInjectWithdrawRate: 0.0, maxInjectWithdrawRate: 0.0))
                }),
                (period: forcedInjectionStart, injectWithdrawRanges: new List<InjectWithdrawRangeByInventory>
                {
                    (inventory: minInventory, (minInjectWithdrawRate: forcedInjectionRate, maxInjectWithdrawRate: forcedInjectionRate)),
                    (inventory: maxInventory, (minInjectWithdrawRate: forcedInjectionRate, maxInjectWithdrawRate: forcedInjectionRate))
                }),
                (period: forcedInjectionStart.Offset(forcedInjectionNumDays), injectWithdrawRanges: new List<InjectWithdrawRangeByInventory>
                {
                    (inventory: minInventory, (minInjectWithdrawRate: 0.0, maxInjectWithdrawRate: 0.0)),
                    (inventory: maxInventory, (minInjectWithdrawRate: 0.0, maxInjectWithdrawRate: 0.0))
                }),
                (period: forcedWithdrawalStart, injectWithdrawRanges: new List<InjectWithdrawRangeByInventory>
                {
                    (inventory: minInventory, (minInjectWithdrawRate: -forcedWithdrawalRate, maxInjectWithdrawRate: -forcedWithdrawalRate)),
                    (inventory: maxInventory, (minInjectWithdrawRate: -forcedWithdrawalRate, maxInjectWithdrawRate: -forcedWithdrawalRate))
                }),
                (period: forcedWithdrawalStart.Offset(forcedWithdrawalNumDays), injectWithdrawRanges: new List<InjectWithdrawRangeByInventory>
                {
                    (inventory: minInventory, (minInjectWithdrawRate: 0.0, maxInjectWithdrawRate: 0.0)),
                    (inventory: maxInventory, (minInjectWithdrawRate: 0.0, maxInjectWithdrawRate: 0.0))
                }),
            };

            Day InjectionCostPaymentTerms(Day injectionDate) => injectionDate.Offset(10);
            Day WithdrawalCostPaymentTerms(Day withdrawalDate) => withdrawalDate.Offset(4);

            CmdtyStorage<Day> storage = CmdtyStorage<Day>.Builder
                .WithActiveTimePeriod(storageStart, storageEnd)
                .WithTimeAndInventoryVaryingInjectWithdrawRatesPolynomial(injectWithdrawConstraints)
                .WithPerUnitInjectionCost(injectionPerUnitCost, InjectionCostPaymentTerms)
                .WithFixedPercentCmdtyConsumedOnInject(injectionCmdtyConsumed)
                .WithPerUnitWithdrawalCost(withdrawalPerUnitCost, WithdrawalCostPaymentTerms)
                .WithFixedPercentCmdtyConsumedOnWithdraw(withdrawalCmdtyConsumed)
                .WithNoCmdtyInventoryLoss()
                .WithNoInventoryCost()
                .WithTerminalInventoryNpv((cmdtySpotPrice, inventory) => 0.0)
                .Build();

            const int numInventorySpacePoints = 500;
            const int numSims = 1_000;
            const int seed = 11;
            
            var lsmcParams = new LsmcValuationParameters<Day>.Builder
            {
                CurrentPeriod = currentDate,
                Inventory = 0.0,
                ForwardCurve = forwardCurve,
                Storage = storage,
                SettleDateRule = deliveryDate => settlementDates[Month.FromDateTime(deliveryDate.Start)],
                DiscountFactors = StorageHelper.CreateAct65ContCompDiscounter(interestRate),
                GridCalc = FixedSpacingStateSpaceGridCalc.CreateForFixedNumberOfPointsOnGlobalInventoryRange(storage, numInventorySpacePoints),
                BasisFunctions = _oneFactorBasisFunctions,
            }
            .SimulateWithMultiFactorModelAndMersenneTwister(MultiFactorParameters.For1Factor(meanReversion, spotVolCurve), numSims, seed)
            .Build();

            LsmcStorageValuationResults<Day> valuationResults = LsmcStorageValuation.WithNoLogger.Calculate(lsmcParams);

            // Calculate the NPV Manually

            // Period of forced injection
            double injectionPv = 0.0;
            for (int i = 0; i < forcedInjectionNumDays; i++)
            {
                Day injectionDate = forcedInjectionStart.Offset(i);
                double forwardPrice = forwardCurve[injectionDate];

                Day cmdtySettlementDate = settlementDates[Month.FromDateTime(injectionDate.Start)];
                double cmdtyDiscountFactor =
                    Act365ContCompoundDiscountFactor(currentDate, cmdtySettlementDate, interestRate);

                Day injectionCostSettlementDate = InjectionCostPaymentTerms(injectionDate);
                double injectCostDiscountFactor =
                    Act365ContCompoundDiscountFactor(currentDate, injectionCostSettlementDate, interestRate);

                double cmdtyBoughtPv = -forwardPrice * forcedInjectionRate * (1 + injectionCmdtyConsumed) * cmdtyDiscountFactor;
                double injectCostPv = -injectionPerUnitCost * forcedInjectionRate * injectCostDiscountFactor;

                injectionPv += cmdtyBoughtPv + injectCostPv;
            }

            // Period of forced withdrawal
            double withdrawalPv = 0.0;
            for (int i = 0; i < forcedWithdrawalNumDays; i++)
            {
                Day withdrawalDate = forcedWithdrawalStart.Offset(i);
                double forwardPrice = forwardCurve[withdrawalDate];

                Day cmdtySettlementDate = settlementDates[Month.FromDateTime(withdrawalDate.Start)];
                double cmdtyDiscountFactor =
                    Act365ContCompoundDiscountFactor(currentDate, cmdtySettlementDate, interestRate);

                Day withdrawalCostSettlementDate = WithdrawalCostPaymentTerms(withdrawalDate);
                double withdrawalCostDiscountFactor =
                    Act365ContCompoundDiscountFactor(currentDate, withdrawalCostSettlementDate, interestRate);

                double cmdtySoldPv = forwardPrice * forcedWithdrawalRate * (1 - withdrawalCmdtyConsumed) * cmdtyDiscountFactor;
                double withdrawalCostPv = -withdrawalPerUnitCost * forcedWithdrawalRate * withdrawalCostDiscountFactor;

                withdrawalPv += cmdtySoldPv + withdrawalCostPv;
            }

            double expectedNpv = injectionPv + withdrawalPv;
            double percentError = (valuationResults.Npv - expectedNpv) / expectedNpv;
            Assert.InRange(percentError, -percentageTol, percentageTol);
        }

        private static double Act365ContCompoundDiscountFactor(Day currentDate, Day paymentDate, double interestRate)
        {
            return Math.Exp(-paymentDate.OffsetFrom(currentDate) / 365.0 * interestRate);
        }

        [Fact]
        [Trait("Category", "Lsmc.Deltas")]
        public void Calculate_DiscountDeltasTrue_DeltasEqualUndiscountedDeltasTimeDiscountFactor()
        {
            const int numSims = 100;
            var paramsBuilder = _1FactorParamsBuilder.Clone()
                .SimulateWithMultiFactorModelAndMersenneTwister(MultiFactorParameters.For1Factor(16.5, _oneFactorFlatSpotVols), numSims, RandomSeed);
            paramsBuilder.Storage = _simpleDailyStorage;
            LsmcValuationParameters<Day> lsmcParamsDiscountDeltasFalse = paramsBuilder.Build();
            paramsBuilder.SimulateWithMultiFactorModelAndMersenneTwister(MultiFactorParameters.For1Factor(16.5, _oneFactorFlatSpotVols), numSims, RandomSeed);
            paramsBuilder.DiscountDeltas = true;
            LsmcValuationParameters<Day> lsmcDiscountDeltasTrue = paramsBuilder.Build();

            LsmcStorageValuationResults<Day> lsmcResultsDeltasNotDiscounted = LsmcStorageValuation.WithNoLogger.Calculate(lsmcParamsDiscountDeltasFalse);
            LsmcStorageValuationResults<Day> lsmcResultsDeltasDiscounted = LsmcStorageValuation.WithNoLogger.Calculate(lsmcDiscountDeltasTrue);

            foreach (Day day in lsmcResultsDeltasNotDiscounted.Deltas.Indices)
            {
                double deltaDiscounted = lsmcResultsDeltasDiscounted.Deltas[day];
                double deltaNotDiscounted = lsmcResultsDeltasNotDiscounted.Deltas[day];
                Day settleDate = paramsBuilder.SettleDateRule(day);
                double discountFactor = paramsBuilder.DiscountFactors(paramsBuilder.CurrentPeriod, settleDate);
                double expectedDiscountedDelta = deltaNotDiscounted * discountFactor;
                Assert.Equal(expectedDiscountedDelta, deltaDiscounted);
            }
        }

        [Fact]
        [Trait("Category", "Lsmc.Ancillary")]
        public void Calculate_OnProgressCalledWithArgumentsInAscendingOrderBetweenZeroAndOne()
        {
            const int numSims = 100;
            var progresses = new List<double>();
            var paramsBuilder = _1FactorParamsBuilder.Clone()
                .SimulateWithMultiFactorModelAndMersenneTwister(MultiFactorParameters.For1Factor(16.5, _oneFactorFlatSpotVols), numSims, RandomSeed);
            paramsBuilder.Storage = _simpleDailyStorage;
            paramsBuilder.OnProgressUpdate = progressPcnt => progresses.Add(progressPcnt);
            var lsmcParams = paramsBuilder.Build();

            // ReSharper disable once UnusedVariable
            LsmcStorageValuationResults<Day> lsmcResults = LsmcStorageValuation.WithNoLogger.Calculate(lsmcParams);

            Assert.InRange(progresses[0], 0.0, 1.0);
            // ReSharper disable once UseIndexFromEndExpression
            Assert.Equal(1.0, progresses[progresses.Count - 1]);

            foreach ((double progress, double nextProgress) in progresses.Zip(progresses.Skip(1), (progress, nextProgress) => (progress, nextProgress)))
            {
                bool isAscending = nextProgress > progress;
                Assert.True(isAscending);
            }
        }

        [Fact]
        [Trait("Category", "Lsmc.Ancillary")]
        public void Calculate_CancelCalls_ThrowsOperationCanceledException()
        {
            const int numSims = 5_000; // Large number of sims to ensure valuation doesn't finish cancel called
            var cancellationTokenSource = new CancellationTokenSource();

            var paramsBuilder = _1FactorParamsBuilder.Clone();
            paramsBuilder.Storage = _simpleDailyStorage;
            paramsBuilder.CancellationToken = cancellationTokenSource.Token;
            paramsBuilder.SimulateWithMultiFactorModelAndMersenneTwister(MultiFactorParameters.For1Factor(16.5, _oneFactorFlatSpotVols), numSims, RandomSeed);
            LsmcValuationParameters<Day> lsmcParams = paramsBuilder.Build();

            Assert.Throws<OperationCanceledException>(() =>
            {
                cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(0.5));
                // ReSharper disable once UnusedVariable
                LsmcStorageValuationResults<Day> lsmcResults = LsmcStorageValuation.WithNoLogger.Calculate(lsmcParams);
            });
        }


        [Fact(Skip = "Failing, needs further investigation")]
        [Trait("Category", "Lsmc.TriggerPrices")]
        public void Calculate_SimpleStorage1Factor_WithdrawTriggerPricesIncreaseWithAbsVolume()
        {
            LsmcValuationParameters<Day> lsmcParams = _triggerPriceTestParamsBuilder.Build();
            LsmcStorageValuationResults<Day> lsmcResults = LsmcStorageValuation.WithNoLogger.Calculate(lsmcParams);

            const double tol = 1E-8;

            foreach (Day day in lsmcResults.TriggerPriceVolumeProfiles.Indices)
            {
                IReadOnlyList<TriggerPricePoint> withdrawTriggerPrices = lsmcResults.TriggerPriceVolumeProfiles[day].WithdrawTriggerPrices;
                for (int i = 1; i < withdrawTriggerPrices.Count; i++)
                {
                    Assert.True(withdrawTriggerPrices[i].Volume < withdrawTriggerPrices[i - 1].Volume);
                    Assert.True(withdrawTriggerPrices[i].Price >= withdrawTriggerPrices[i - 1].Price - tol);
                }
            }
        }

        [Fact]
        [Trait("Category", "Lsmc.TriggerPrices")]
        public void Calculate_SimpleStorage1Factor_WithdrawTriggerPriceHigherThanInjectTriggerPrice()
        {
            LsmcValuationParameters<Day> lsmcParams = _triggerPriceTestParamsBuilder.Build();
            LsmcStorageValuationResults<Day> lsmcResults = LsmcStorageValuation.WithNoLogger.Calculate(lsmcParams);
            foreach (TriggerPrices triggerPrices in lsmcResults.TriggerPrices.Data)
                if (triggerPrices.HasWithdrawPrice && triggerPrices.HasInjectPrice)
                    Assert.True(triggerPrices.MaxWithdrawTriggerPrice > triggerPrices.MaxInjectTriggerPrice);
        }

        [Fact]
        [Trait("Category", "Lsmc.TriggerPrices")]
        public void Calculate_SimpleStorage1FactorZeroInventory_NoWithdrawTriggerPriceOnFirstPeriod()
        {
            LsmcValuationParameters<Day>.Builder paramsBuilder = _triggerPriceTestParamsBuilder.Clone();
            paramsBuilder.Inventory = 0.0;
            LsmcValuationParameters<Day> lsmcParams = paramsBuilder.Build();
            LsmcStorageValuationResults<Day> lsmcResults = LsmcStorageValuation.WithNoLogger.Calculate(lsmcParams);

            TriggerPrices firstTriggerPrices = lsmcResults.TriggerPrices[0];
            Assert.False(firstTriggerPrices.HasWithdrawPrice);
            Assert.Null(firstTriggerPrices.MaxWithdrawVolume);
            Assert.Null(firstTriggerPrices.MaxWithdrawTriggerPrice);
            Assert.Equal(0, lsmcResults.TriggerPriceVolumeProfiles[0].WithdrawTriggerPrices.Count);
        }

        [Fact]
        [Trait("Category", "Lsmc.TriggerPrices")]
        public void Calculate_SimpleStorage1FactorInventoryFull_NoInjectTriggerPriceOnFirstPeriod()
        {
            LsmcValuationParameters<Day>.Builder paramsBuilder = _triggerPriceTestParamsBuilder.Clone();
            paramsBuilder.Inventory = 52_500.0;
            LsmcValuationParameters<Day> lsmcParams = paramsBuilder.Build();
            LsmcStorageValuationResults<Day> lsmcResults = LsmcStorageValuation.WithNoLogger.Calculate(lsmcParams);

            TriggerPrices firstTriggerPrices = lsmcResults.TriggerPrices[0];
            Assert.False(firstTriggerPrices.HasInjectPrice);
            Assert.Null(firstTriggerPrices.MaxInjectTriggerPrice);
            Assert.Null(firstTriggerPrices.MaxInjectVolume);
            Assert.Equal(0, lsmcResults.TriggerPriceVolumeProfiles[0].InjectTriggerPrices.Count);
        }

        [Fact]
        [Trait("Category", "Lsmc.TriggerPrices")]
        public void Calculate_SimpleStorage1Factor_TriggerVolumesConsistentWithMaxInjectWithdrawRate()
        {
            LsmcValuationParameters<Day> lsmcParams = _triggerPriceTestParamsBuilder.Build();
            LsmcStorageValuationResults<Day> lsmcResults = LsmcStorageValuation.WithNoLogger.Calculate(lsmcParams);

            const double maxWithdrawalRate = 850.0;
            const double maxInjectionRate = 625.0;
            const double maxInventory = 52_500.0;
            const double minInventory = 0.0;

            foreach ((Day day, TriggerPrices triggerPrices) in lsmcResults.TriggerPrices)
            {
                TriggerPriceVolumeProfiles triggerPriceProfile = lsmcResults.TriggerPriceVolumeProfiles[day];
                double expectedInventory = lsmcResults.ExpectedStorageProfile[day].Inventory;
                if (triggerPrices.HasInjectPrice)
                {
                    double expectedMaxInjectVolume = Math.Min(maxInjectionRate, maxInventory - expectedInventory);
                    Assert.Equal(expectedMaxInjectVolume, triggerPrices.MaxInjectVolume);
                    Assert.Equal(expectedMaxInjectVolume, triggerPriceProfile.InjectTriggerPrices.Max(point => point.Volume));
                }
                else
                    Assert.Equal(0, triggerPriceProfile.InjectTriggerPrices.Count);

                if (triggerPrices.HasWithdrawPrice)
                {
                    double expectedMaxWithdrawVolume = Math.Min(maxWithdrawalRate, expectedInventory - minInventory);
                    Assert.Equal(expectedMaxWithdrawVolume, -triggerPrices.MaxWithdrawVolume);
                    Assert.Equal(expectedMaxWithdrawVolume, -triggerPriceProfile.WithdrawTriggerPrices.Min(point => point.Volume));
                }
                else
                    Assert.Equal(0, triggerPriceProfile.WithdrawTriggerPrices.Count);
            }
        }

    }
}
