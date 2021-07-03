/*
MIT LICENSE

Copyright 2017 Digital Ruby, LLC - http://www.digitalruby.com

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ExchangeSharp.BinanceGroup;
using Newtonsoft.Json.Linq;

namespace ExchangeSharp
{
	public sealed class ExchangeTRBinanceAPI : BinanceGroupCommon
	{
		public override string BaseUrl { get; set; } = "https://api.binance.cc/api/v3";
		public override string BaseUrlWebSocket { get; set; } = "wss://stream.binance.cc";
		public override string BaseUrlPrivate { get; set; } = "https://www.trbinance.com";
		public override string WithdrawalUrlPrivate { get; set; } = "https://api.binance.com/api/v3";
		public override string BaseWebUrl { get; set; } = "https://www.trbinance.com";

		protected override async Task<IEnumerable<ExchangeOrderResult>> OnGetCompletedOrderDetailsAsync(string? marketSymbol = null, DateTime? afterDate = null)
		{
			//new way
			List<ExchangeOrderResult> trades = new List<ExchangeOrderResult>();
			if (string.IsNullOrWhiteSpace(marketSymbol))
			{
				trades.AddRange(await base.GetCompletedOrdersForAllSymbolsAsync(afterDate));
			}
			else
			{
				Dictionary<string, object> payload = await base.GetNoncePayloadAsync();
				payload["symbol"] = marketSymbol!;
				if (afterDate != null)
				{
					payload["startTime"] = afterDate.Value.UnixTimestampFromDateTimeMilliseconds();
				}
				JToken token = await base.MakeJsonRequestAsync<JToken>("/open/v1/orders/trades", BaseUrlPrivate, payload);
				foreach (JToken trade in token.Children().Children().Children())
				{
					trades.Add(ParseTrade(trade, marketSymbol!));
				}
			}
			return trades;
		}

		protected override async Task<ExchangeOrderResult> OnPlaceOrderAsync(ExchangeOrderRequest order)
		{
			Dictionary<string, object> payload = await GetNoncePayloadAsync();
			payload["symbol"] = order.MarketSymbol;
			payload["newClientOrderId"] = order.ClientOrderId;
			payload["side"] = order.IsBuy ? 0 : 1;
			//if (order.OrderType == OrderType.Stop)
			//	payload["type"] = "STOP_LOSS";//if order type is stop loss/limit, then binance expect word 'STOP_LOSS' inestead of 'STOP'
			//else
			//	payload["type"] = order.OrderType.ToStringUpperInvariant();

			payload["type"] = 1;

			// Binance has strict rules on which prices and quantities are allowed. They have to match the rules defined in the market definition.
			decimal outputQuantity = await ClampOrderQuantity(order.MarketSymbol, order.Amount);
			decimal outputPrice = await ClampOrderPrice(order.MarketSymbol, order.Price);

			// Binance does not accept quantities with more than 20 decimal places.
			payload["quantity"] = Math.Round(outputQuantity, 20);
			//payload["newOrderRespType"] = "FULL";

			if (order.OrderType != OrderType.Market)
			{
				//payload["timeInForce"] = "GTC";
				payload["price"] = outputPrice;
			}
			order.ExtraParameters.CopyTo(payload);

			JToken? token = await MakeJsonRequestAsync<JToken>("/open/v1/orders", BaseUrlPrivate, payload, "POST");
			if (token is null)
			{
				return null;
			}
			return base.ParseOrder(token);
		}

		public override string NormalizeMarketSymbol(string marketSymbol)
		{
			return marketSymbol;
		}

		protected override async Task<IEnumerable<ExchangeOrderResult>> OnGetOpenOrderDetailsAsync(string? marketSymbol = null)
		{
			List<ExchangeOrderResult> orders = new List<ExchangeOrderResult>();
			Dictionary<string, object> payload = await GetNoncePayloadAsync();
			if (!string.IsNullOrWhiteSpace(marketSymbol))
			{
				payload["symbol"] = marketSymbol!;
			}

			payload["type"] = 1;
			JToken token = await MakeJsonRequestAsync<JToken>("/open/v1/orders", BaseUrlPrivate, payload);
			foreach (JToken order in token.Children().Children().Children())
			{
				orders.Add(ParseOrder(order));
			}

			return orders;
		}

		protected override async Task<ExchangeOrderResult> OnGetOrderDetailsAsync(string orderId, string? marketSymbol = null)
		{
			Dictionary<string, object> payload = await GetNoncePayloadAsync();
			if (string.IsNullOrWhiteSpace(marketSymbol))
			{
				throw new InvalidOperationException("Binance single order details request requires symbol");
			}
			payload["symbol"] = marketSymbol!;
			payload["orderId"] = orderId;
			JToken token = await MakeJsonRequestAsync<JToken>("/open/v1/orders/detail", BaseUrlPrivate, payload);
			ExchangeOrderResult result = ParseOrder(token);

			//// Add up the fees from each trade in the order
			//Dictionary<string, object> feesPayload = await GetNoncePayloadAsync();
			//feesPayload["symbol"] = marketSymbol!;
			//JToken feesToken = await MakeJsonRequestAsync<JToken>("/open/v1/orders/trades", BaseUrlPrivate, feesPayload);
			//ParseFees(feesToken, result);

			return result;
		}

		protected override async Task OnCancelOrderAsync(string orderId, string? marketSymbol = null)
		{
			Dictionary<string, object> payload = await GetNoncePayloadAsync();
			payload["orderId"] = orderId;
			_ = await MakeJsonRequestAsync<JToken>("/open/v1/orders/cancel", BaseUrlPrivate, payload, "POST");
		}

		protected override async Task<ExchangeOrderBook> OnGetOrderBookAsync(string marketSymbol, int maxCount = 10)
		{
			Dictionary<string, object> payload = await GetNoncePayloadAsync();
			payload["symbol"] = marketSymbol;
			payload["limit"] = maxCount;
			JToken obj = await MakeJsonRequestAsync<JToken>("/open/v1/market/depth", BaseUrlPrivate, payload, "GET");
			return ExchangeAPIExtensions.ParseOrderBookFromJTokenArrays(obj, sequence: "lastUpdateId", maxCount: maxCount);
		}

		private ExchangeOrderResult ParseTrade(JToken token, string symbol)
		{
			ExchangeOrderResult result = new ExchangeOrderResult
			{
				Result = ExchangeAPIOrderResult.Filled,
				Amount = token["qty"].ConvertInvariant<decimal>(),
				AmountFilled = token["qty"].ConvertInvariant<decimal>(),
				Price = token["price"].ConvertInvariant<decimal>(),
				AveragePrice = token["price"].ConvertInvariant<decimal>(),
				IsBuy = token["isBuyer"].ConvertInvariant<bool>() == true,
				OrderDate = CryptoUtility.UnixTimeStampToDateTimeMilliseconds(token["time"].ConvertInvariant<long>()),
				OrderId = token["orderId"].ToStringInvariant(),
				TradeId = token["tradeId"].ToStringInvariant(),
				Fees = token["commission"].ConvertInvariant<decimal>(),
				FeesCurrency = token["commissionAsset"].ToStringInvariant(),
				MarketSymbol = symbol
			};

			return result;
		}

		private ExchangeOrderResult ParseOrder(JToken token)
		{
			ExchangeOrderResult result = new ExchangeOrderResult
			{
				Amount = token["origQty"].ConvertInvariant<decimal>(),
				AmountFilled = token["executedQty"].ConvertInvariant<decimal>(),
				Price = token["price"].ConvertInvariant<decimal>(),
				IsBuy = token["side"].ConvertInvariant<int>() == 0,
				//OrderDate = CryptoUtility.UnixTimeStampToDateTimeMilliseconds(token["time"].ConvertInvariant<long>(token["transactTime"].ConvertInvariant<long>())),
				OrderId = token["orderId"].ToStringInvariant(),
				MarketSymbol = token["symbol"].ToStringInvariant(),
				//ClientOrderId = token["clientOrderId"].ToStringInvariant()
			};

			result.Result = ParseExchangeAPIOrderResult(token["status"].ToStringInvariant(), result.AmountFilled);

			return result;
		}
	}

	public partial class ExchangeName { public const string TRBinance = "TRBinance"; }

}
