﻿#region License
// Copyright (c) 2021 Jake Fowler
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
using ExcelDna.Integration;

namespace Cmdty.Storage.Excel
{
    abstract class CalcWrapperObservableBase : IExcelObservable, IDisposable
    {
        protected readonly ExcelCalcWrapper _calcWrapper;
        protected IExcelObserver _observer;

        protected CalcWrapperObservableBase(ExcelCalcWrapper calcWrapper)
        {
            _calcWrapper = calcWrapper;
            _calcWrapper.CalcTask.ContinueWith(task =>
            {
                _observer?.OnCompleted(); // TODO this could not get called if invoked before Subscribe. Does this matter?
            });
        }

        public IDisposable Subscribe(IExcelObserver excelObserver)
        {
            _observer = excelObserver;
            OnSubscribe();
            return this;
        }

        protected abstract void OnSubscribe();
        
        protected virtual void OnDispose() => _observer = null;

        public void Dispose() => OnDispose();

    }
}
