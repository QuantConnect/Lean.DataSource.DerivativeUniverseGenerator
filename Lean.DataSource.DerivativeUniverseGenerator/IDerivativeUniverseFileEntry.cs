/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using QuantConnect;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Indicators;

namespace Lean.DataSource.DerivativeUniverseGenerator
{
    public interface IDerivativeUniverseFileEntry
    {
        Symbol Symbol { get; set; }

        decimal Open { get; set; }

        decimal High { get; set; }

        decimal Low { get; set; }

        decimal Close { get; set; }

        decimal Volume { get; set; }

        string ToCsv();
    }

    public class BaseDerivativeUniverseFileEntry : IDerivativeUniverseFileEntry
    {
        public Symbol Symbol { get; set; }

        public decimal Open { get; set; }

        public decimal High { get; set; }

        public decimal Low { get; set; }

        public decimal Close { get; set; }

        public decimal Volume { get; set; }

        public BaseDerivativeUniverseFileEntry(Symbol symbol)
        {
            Symbol = symbol;
        }

        public BaseDerivativeUniverseFileEntry(Symbol symbol, Slice data)
        {
            Symbol = symbol;

            // Try getting the data from a trade bar
            if (data.Bars.TryGetValue(symbol, out var tradeBar))
            {
                Open = tradeBar.Open;
                High = tradeBar.High;
                Low = tradeBar.Low;
                Close = tradeBar.Close;
                Volume = tradeBar.Volume;
            }
            else if (data.QuoteBars.TryGetValue(symbol, out var quoteBar))
            {
                Open = quoteBar.Open;
                High = quoteBar.High;
                Low = quoteBar.Low;
                Close = quoteBar.Close;
            }
        }

        public virtual string ToCsv()
        {
            var sid = Symbol.ID.ToString();
            if (Symbol.SecurityType.IsOption())
            {
                sid = sid.Replace($"|{Symbol.Underlying.ID}", "");
            }

            return $"{sid},{Symbol.Value},{Open},{High},{Low},{Close},{Volume}";
        }
    }
}
