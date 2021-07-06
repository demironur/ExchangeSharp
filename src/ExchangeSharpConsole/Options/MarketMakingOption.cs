using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommandLine;
using ExchangeSharp;
using ExchangeSharpConsole.Options.Interfaces;

namespace ExchangeSharpConsole.Options
{
	[Verb("marketmaker", HelpText = "market maker bot.")]
	public class MarketMakingOption : BaseOption, IOptionWithKey
	{
		public IExchangeAPI api { get; set; } = ExchangeAPI.GetExchangeAPI(ExchangeName.TRBinance);
		public string KeyPath { get; set; }
		//public string NormalizedMarketSymbol { get; set; } = "USDTTRY";
		//public string MarketSymbol { get; set; } = "USDT_TRY";
		//public decimal OrderCancelLevel { get; set; } = 0.007m;
		//public int OrderDivideCount { get; set; } = 5;
		//public decimal OrderStepSize { get; set; } = 0.001m;
		//public decimal OrderMinSpread { get; set; } = 0.002m;
		//public decimal OrderAmount { get; set; } = 10m;
		public string NormalizedMarketSymbol { get; set; } = "BUSDTRY";
		public string MarketSymbol { get; set; } = "BUSD_TRY";
		public decimal OrderCancelLevel { get; set; } = 0.01m;
		public int OrderDivideCount { get; set; } = 1;
		public decimal OrderStepSize { get; set; } = 0.01m;
		public decimal OrderMinSpread { get; set; } = 0.0m;
		public decimal OrderAmount { get; set; } = 100m;

		public override async Task RunCommand()
		{
			GetExchangeApi();

			while (true)//net worth not less than 100try
			{
				//ExchangeOrderBook orderbook = await GetOrderBooks();
				var ticker = GetTicker().Result;

				BuyAndSellOrders(ticker);
				Console.WriteLine("{0}: BuyAndSellOrders done", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
				var openOrders = await GetOpenOrders();
				Console.WriteLine("{0}: GetOpenOrders done", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));

				foreach (var order in openOrders)
				{
					if (order.Price < ticker.Bid - OrderCancelLevel || order.Price > ticker.Ask + OrderCancelLevel)
					{
						await CancelOrder(order.OrderId);
					}
				}

				Console.WriteLine("{0}: CancelOrders done", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));

				await Task.Delay(10);
			}


			//var orderBook = await api.GetCompletedOrderDetailsAsync("USDT_TRY", new DateTime(2021, 05, 18), false);
		}

		public void BuyAndSellOrders(ExchangeTicker ticker)
		{
			for (int i = 0; i < OrderDivideCount; i++)
			{
				var buyResult = PlaceOrder(ticker.Bid - (OrderMinSpread +i * OrderStepSize), true)/*.Result*/;
				//GetOrderResult(buyResult, true);

				var sellResult = PlaceOrder(ticker.Ask + (OrderMinSpread + i * OrderStepSize), false)/*.Result*/;
				//GetOrderResult(sellResult, false);
			}
		}

		public IExchangeAPI GetExchangeApi()
		{
			//using var api = ExchangeAPI.GetExchangeAPI(ExchangeName.TRBinance);

			try
			{
				api.LoadAPIKeys(KeyPath);
			}
			catch (ArgumentException)
			{
				Console.Error.WriteLine(
					"Invalid key file.\n" +
					"Try generating a key file with the \"keys\" utility."
				);

				Environment.Exit(Program.ExitCodeError);
				//return;
			}

			return api;
		}

		public async Task<ExchangeTicker> GetTicker()
		{
			var ticker = await api.GetTickerAsync(NormalizedMarketSymbol);
			Console.WriteLine("{4}: {2}{3}. Bid:{0} - Ask:{1}", ticker.Bid, ticker.Ask, "USD", "TRY", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
			return ticker;
		}

		public async Task/*<ExchangeOrderResult>*/ PlaceOrder(decimal price, bool isBuy)
		{
			var result = /*await*/ api.PlaceOrderAsync(new ExchangeOrderRequest
			{
				Amount = OrderAmount,
				IsBuy = isBuy,
				Price = price,
				MarketSymbol = MarketSymbol
			});

			//return result;
		}

		public async Task GetOrderResult(ExchangeOrderResult result, bool isBuy)
		{
			result = await api.GetOrderDetailsAsync(result.OrderId, MarketSymbol);

			Console.WriteLine(
				"Placed an {3} order on Binance for 10 usdt at {0} try. Status is {1}. Order id is {2}.",
				result.Price, result.Result, result.OrderId, isBuy ? "Buy" : "Sell"
			);
		}

		public async Task CancelOrder(string OrderId)
		{
			api.CancelOrderAsync(OrderId, MarketSymbol);
			Console.WriteLine("Cancel OrderId:{0}", OrderId);
		}

		public async Task<IEnumerable<ExchangeOrderResult>> GetOpenOrders()
		{
			var openOrders = await api.GetOpenOrderDetailsAsync(MarketSymbol);
			return openOrders;
		}

		public async Task<ExchangeOrderBook> GetOrderBooks()
		{
			var orderBook = await api.GetOrderBookAsync(MarketSymbol, 100);

			return orderBook;
		}
	}
}
