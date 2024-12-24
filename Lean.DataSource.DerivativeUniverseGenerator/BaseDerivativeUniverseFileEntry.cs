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

using QuantConnect.Data;

namespace QuantConnect.DataSource.DerivativeUniverseGenerator
{
    /// <summary>
    /// Interface for a constituent entry in the derivative universe file.
    /// </summary>
    public interface IDerivativeUniverseFileEntry
    {
        /// <summary>
        /// Symbol of the entry.
        /// </summary>
        Symbol Symbol { get; set; }

        /// <summary>
        /// Update the entry with the data from the slice.
        /// </summary>
        void Update(Slice data);

        /// <summary>
        /// Convert the entry to a CSV string.
        /// </summary>
        string ToCsv();

        /// <summary>
        /// Gets the header of the CSV file, which will be added as a comment line at the beginning of the file, prefixed with '#'.
        /// </summary>
        string GetHeader();
    }

    /// <summary>
    /// Base implementation of <see cref="IDerivativeUniverseFileEntry"/>.
    /// </summary>
    public class BaseDerivativeUniverseFileEntry : IDerivativeUniverseFileEntry
    {
        /// <summary>
        /// Symbol of the entry.
        /// </summary>
        public Symbol Symbol { get; set; }

        /// <summary>
        /// Open price on the processing date.
        /// </summary>
        public decimal Open { get; set; }

        /// <summary>
        /// High price on the processing date.
        /// </summary>
        public decimal High { get; set; }

        /// <summary>
        /// Low price on the processing date.
        /// </summary>
        public decimal Low { get; set; }

        /// <summary>
        /// Close price on the processing date.
        /// </summary>
        public decimal Close { get; set; }

        /// <summary>
        /// Volume on the processing date.
        /// </summary>
        public decimal Volume { get; set; }

        /// <summary>
        /// Creates a new instance of the <see cref="BaseDerivativeUniverseFileEntry"/> class.
        /// </summary>
        /// <param name="symbol">The symbol</param>
        public BaseDerivativeUniverseFileEntry(Symbol symbol)
        {
            Symbol = symbol;
        }

        /// <summary>
        /// Update the entry with the data from the slice.
        /// </summary>
        public virtual void Update(Slice data)
        {
            if (data.Bars.TryGetValue(Symbol, out var tradeBar))
            {
                Open = tradeBar.Open;
                High = tradeBar.High;
                Low = tradeBar.Low;
                Close = tradeBar.Close;
                Volume = tradeBar.Volume;
            }
            else if (data.QuoteBars.TryGetValue(Symbol, out var quoteBar))
            {
                Open = quoteBar.Open;
                High = quoteBar.High;
                Low = quoteBar.Low;
                Close = quoteBar.Close;
            }
        }

        /// <summary>
        /// Convert the entry to a CSV string.
        /// </summary>
        public virtual string ToCsv()
        {
            return $"{Symbol.ID},{Symbol.Value},{Open},{High},{Low},{Close},{Volume}";
        }

        /// <summary>
        /// Gets the header of the CSV file
        /// </summary>
        public virtual string GetHeader()
        {
            return "symbol_id,symbol_value,open,high,low,close,volume";
        }
    }
}
