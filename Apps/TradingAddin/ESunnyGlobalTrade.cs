using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Windows.Forms;
using ExcelDna.Integration;
using TradingAddin.Properties;

namespace ESunny.Global.Trade
{
  // OrderInfo: exchange, commodity, contract, type, side, price, quantity, state, placed-time, updated-time, filled-quantity
  using OrderInfoTuple = Tuple<string, string, string, char, char, double, uint, Tuple<char, string, string, uint>>;
  // FillInfo: orderNo, exchange, commodity, contract, side, price, quantity, timestamp
  using FillInfoTuple = Tuple<string, string, string, string, char, double, uint, Tuple<string>>;
  // PositionKey: exchange, commodity, contract
  using PositionKeyTuple = Tuple<string, string, string>;

  // Contains the API singleton
  public static class APIInstance
  {
    public static ITapTradeAPI api = null;
    public static EventCallback callback = null;
    public static Dictionary<string, OrderInfoTuple> orderInfoMap = new Dictionary<string, OrderInfoTuple>();
    public static Dictionary<string, FillInfoTuple> fillInfoMap = new Dictionary<string, FillInfoTuple>();
    public static Dictionary<PositionKeyTuple, int> positionMap = new Dictionary<PositionKeyTuple, int>();

    private static object isConnected = false;
    private static string errorText = "";
    private static AutoResetEvent resetEvent = new AutoResetEvent(false);

    [ExcelFunction(Category = "TradingPlatform", Name = "TP.ESunnyGlobal.Login", Description = "Login to API")]
    public static object Login()
    {
      if (ExcelDnaUtil.IsInFunctionWizard()) return "#ERR: In function wizard.";

      if (api != null) return "API is ready.";

      // Create and intialize API
      var appInfo = new TapAPIApplicationInfo
      {
        AuthCode = Settings.Default.ESunny_Global_Trade_AuthCode,
        KeyOperationLogPath = Path.GetTempPath()
      };

      void cleanup()
      {
        if (api != null) External.FreeITapTradeAPI(api);
        api = null;
      }

      api = External.CreateITapTradeAPI(appInfo, out int errorCode);
      if (api == null || errorCode != External.TAPIERROR_SUCCEED)
      {
        cleanup();
        return "#ERR: Failed to create API: error code is " + errorCode;
      }

      var ip = Settings.Default.ESunny_Global_Trade_IP;
      var port = Settings.Default.ESunny_Global_Trade_Port;
      errorCode = api.SetHostAddress(ip, port);
      if (errorCode != External.TAPIERROR_SUCCEED)
      {
        cleanup();
        return "#ERR: Failed to set host address: error code is " + errorCode;
      }

      if (callback != null)
      {
        resetEvent.Reset(); // reset to nonsignaled, causing threads to block
        api.SetAPINotify(callback); // reuse callback when API is reconnected (i.e., set to null)
      }
      else
      {
        callback = new EventCallback();
        api.SetAPINotify(callback);

        // Handler for OnRspLogin event
        callback.OnRspLoginEvent += (errCode, loginRspInfo) =>
        {
          if (errCode != External.TAPIERROR_SUCCEED && errCode != External.TAPIERROR_LOGIN_DDA) // ignore TAPIERROR_LOGIN_DDA
          {
            errorText = "#ERR: Failed to login: error code is " + errCode;
            resetEvent.Set();
          }
        };

        // Handler for OnRtnContactInfo event
        callback.OnRtnContactInfoEvent += (errCode, isLast, ContactInfo) =>
        {
          if (errCode != External.TAPIERROR_SUCCEED)
          {
            errorText = "#ERR: Failed to get contact info: error code is " + errCode;
            resetEvent.Set();
          }
          else
          {
            if (ContactInfo == Settings.Default.ESunny_Global_Trade_ContactInfo)
              api.RequestVertificateCode(out uint sessionId, ContactInfo);
            else
            {
              MessageBox.Show("Verification contact is different from the config, please check");
            }
          }
        };

        // Handler for OnRspRequestVertificateCode event
        callback.OnRspRequestVertificateCodeEvent += (sessionId, errCode, rsp) =>
        {
          if (errCode != External.TAPIERROR_SUCCEED)
          {
            errorText = "#ERR: Failed to request verification code: error code is " + errCode;
            resetEvent.Set();
          }
          else
          {
            var code = Microsoft.VisualBasic.Interaction.InputBox("Please type verification code:", "ESunny Global Trade API");
            var req = new TapAPISecondCertificationReq { VertificateCode = code, LoginType = External.TAPI_LOGINTYPE_NORMAL };
            api.SetVertificateCode(out sessionId, req);
          }
        };

        // Handler for OnAPIReady event
        callback.OnAPIReadyEvent += (errCode) =>
        {
          if (errCode != External.TAPIERROR_SUCCEED)
          {
            errorText = "#ERR: API is not ready: error code is " + errCode;
            resetEvent.Set();
          }
          else
          {
            resetEvent.Set();

            var posQryReq = new TapAPIPositionQryReq { AccountNo = Settings.Default.ESunny_Global_Trade_UserNo };
            api.QryPositionSummary(out uint sessionId, posQryReq); // only allowed once

            var orderQuery = new TapAPIOrderQryReq();
            api.QryOrder(out sessionId, orderQuery); // only allowed once

            var fillQryReq = new TapAPIFillQryReq { AccountNo = Settings.Default.ESunny_Global_Trade_UserNo };
            api.QryFill(out sessionId, fillQryReq); // only allowed once
          }
        };

        // Local function used by OnRspQryPositionSummary & OnRtnPositionSummary
        void PositionEventHandler(TapAPIPositionSummary info)
        {
          if (info == null) return;

          lock (positionMap)
          {
            var contractCode = info.ExchangeNo + "," + info.CommodityNo + "," + info.ContractNo;
            var multiplier = (info.MatchSide == External.TAPI_SIDE_BUY) ? 1 : -1;
            var quantity = multiplier * (int)info.PositionQty;

            positionMap[Tuple.Create(info.ExchangeNo, info.CommodityNo, info.ContractNo)] = quantity;
          }
        }

        // Handler for OnRspQryPositionSummary event
        callback.OnRspQryPositionSummaryEvent += (sessionId, errCode, isLast, info) =>
        {
          if (errCode != External.TAPIERROR_SUCCEED)
            new Thread(() => MessageBox.Show("#ERR: Failed to query position summary: error code is " + errCode)).Start();
          else
            PositionEventHandler(info);
        };

        // Handler for OnRtnPositionSummary event
        callback.OnRtnPositionSummaryEvent += PositionEventHandler;

        // Local function used by OnRspQryOrder & OnRtnOrder
        void OrderEventHandler(TapAPIOrderInfo order)
        {
          if (order == null) return;

          lock (orderInfoMap)
          {
            var item = new OrderInfoTuple(
              string.Copy(order.ExchangeNo),
              string.Copy(order.CommodityNo),
              string.Copy(order.ContractNo2 == "" ? order.ContractNo : order.ContractNo + "/" + order.ContractNo2),
              order.OrderType,
              order.OrderSide,
              order.OrderPrice,
              order.OrderQty,
              new Tuple<char, string, string, uint>(
                order.OrderState,
                string.Copy(order.OrderInsertTime),
                string.Copy(order.OrderUpdateTime),
                order.OrderMatchQty));

            orderInfoMap[order.OrderNo] = item;
          }
        }

        // Handler for OnRspQryOrder event
        callback.OnRspQryOrderEvent += (sessionID, errCode, isLast, order) =>
        {
          if (errCode != External.TAPIERROR_SUCCEED)
            new Thread(() => MessageBox.Show("#ERR: Failed to query orders: error code is " + errCode)).Start();
          else
            OrderEventHandler(order);
        };

        // Handler for OnRtnOrder event
        callback.OnRtnOrderEvent += (info) =>
        {
          if (info.ErrorCode == External.TAPIERROR_SUCCEED)
            OrderEventHandler(info.OrderInfo);
        };

        // Local function used by OnRspQryFill & OnRtnFill
        void FillEventHandler(TapAPIFillInfo fill)
        {
          if (fill == null) return;

          lock (fillInfoMap)
          {
            if (!fillInfoMap.ContainsKey(fill.MatchNo))
            {
              var item = new FillInfoTuple(
                string.Copy(fill.OrderNo),
                string.Copy(fill.ExchangeNo),
                string.Copy(fill.CommodityNo),
                string.Copy(fill.ContractNo),
                fill.MatchSide,
                fill.MatchPrice,
                fill.MatchQty,
                new Tuple<string>(fill.MatchDateTime));

              fillInfoMap.Add(fill.MatchNo, item);
            }
          }
        }

        // Handler for OnRspQryFill event
        callback.OnRspQryFillEvent += (sessionID, errCode, isLast, fill) =>
        {
          if (errCode != External.TAPIERROR_SUCCEED)
            new Thread(() => MessageBox.Show("#ERR: Failed to query fills: error code is " + errCode)).Start();
          else
            FillEventHandler(fill);
        };

        // Handler for OnRtnFill event
        callback.OnRtnFillEvent += (fill) => FillEventHandler(fill);

        // Handler for OnDisconnect event
        callback.OnDisconnectEvent += (reasonCode) =>
        {
          errorText = "#ERR: API disconnected: reason code is " + reasonCode;
          lock (isConnected) isConnected = false;
          resetEvent.Set();
          cleanup();
        };
      }

      // Login to the API
      var loginAuth = new TapAPITradeLoginAuth
      {
        UserNo = Settings.Default.ESunny_Global_Trade_UserNo,
        Password = Settings.Default.ESunny_Global_Trade_Password,
        ISModifyPassword = External.APIYNFLAG_NO
      };

      errorCode = api.Login(loginAuth);
      if (errorCode != External.TAPIERROR_SUCCEED)
      {
        cleanup();
        return "#ERR: Failed to login: error code is " + errorCode;
      }

      resetEvent.WaitOne();

      if (errorText == "")
      {
        isConnected = true;
        return "API is ready.";
      }

      cleanup();
      return errorText;
    }

    [ExcelFunction(Category = "TradingPlatform", Name = "TP.ESunnyGlobal.Logout", Description = "Log out API")]
    public static object Logout()
    {
      if (ExcelDnaUtil.IsInFunctionWizard()) return "#ERR: In function wizard.";

      if (api != null)
      {
        External.FreeITapTradeAPI(api);
        api = null;
      }

      return "API is logged out.";
    }

    [ExcelFunction(Category = "TradingPlatform", Name = "TP.ESunnyGlobal.TradeConnected",
      Description = "Check trade connection")]
    public static object TradeConnected()
    {
      lock (isConnected) return isConnected;
    }
  }

  public static class APIFunctions
  {
    private static bool sendOrderEventHanldersInitialized = false;

    #region Event Handlers
    // Handler of OnRspOrderAction event (triggered by SendOrder/CancleOrder)
    private static void OnRspOrderActionHandler(uint sessionId, int errorCode, TapAPIOrderActionRsp info)
    {
      if (errorCode != External.TAPIERROR_SUCCEED)
      {
        var order = info.OrderInfo;
        var contractCode = order.ExchangeNo + "," + order.CommodityNo + ","
          + (order.ContractNo2 == "" ? order.ContractNo : order.ContractNo + "/" + order.ContractNo2);
        var msg = "#ERR: Failed to send/cancel order for [" + contractCode + "]: error code is " + errorCode;
        new Thread(() => MessageBox.Show(msg)).Start();
      }
    }

    // Handler of OnRtdOrder event (triggered by SendOrder/CancelOrder)
    private static void OnRtnOrderHandler(TapAPIOrderInfoNotice info)
    {
      var order = info.OrderInfo;
      if (info.ErrorCode != External.TAPIERROR_SUCCEED)
      {
        var contractCode = order.ExchangeNo + "," + order.CommodityNo + ","
          + (order.ContractNo2 == "" ? order.ContractNo : order.ContractNo + "/" + order.ContractNo2);
        var msg = "#ERR: Failed to send/cancel order for [" + contractCode + "]: error code is " + info.ErrorCode;
        new Thread(() => MessageBox.Show(msg)).Start();
      }
    }
    #endregion

    private static string TranslateOrderState(char orderState)
    {
      if (orderState == External.TAPI_ORDER_STATE_SUBMIT)
        return "SUBMIT";
      else if (orderState == External.TAPI_ORDER_STATE_ACCEPT)
        return "ACCEPT";
      else if (orderState == External.TAPI_ORDER_STATE_TRIGGERING)
        return "TRIGGERING";
      else if (orderState == External.TAPI_ORDER_STATE_EXCTRIGGERING)
        return "EXCTRIGGERING";
      else if (orderState == External.TAPI_ORDER_STATE_QUEUED)
        return "QUEUED";
      else if (orderState == External.TAPI_ORDER_STATE_PARTFINISHED)
        return "PARTFINISHED";
      else if (orderState == External.TAPI_ORDER_STATE_FINISHED)
        return "FINISHED";
      else if (orderState == External.TAPI_ORDER_STATE_CANCELING)
        return "CANCELING";
      else if (orderState == External.TAPI_ORDER_STATE_MODIFYING)
        return "MODIFYING";
      else if (orderState == External.TAPI_ORDER_STATE_CANCELED)
        return "CANCELED";
      else if (orderState == External.TAPI_ORDER_STATE_LEFTDELETED)
        return "LEFTDELETED";
      else if (orderState == External.TAPI_ORDER_STATE_FAIL)
        return "FAIL";
      else if (orderState == External.TAPI_ORDER_STATE_DELETED)
        return "DELETED";
      else if (orderState == External.TAPI_ORDER_STATE_SUPPENDED)
        return "SUPPENDED";
      else if (orderState == External.TAPI_ORDER_STATE_DELETEDFOREXPIRE)
        return "DELETEDFOREXPIRE";
      else if (orderState == External.TAPI_ORDER_STATE_EFFECT)
        return "EFFECT";
      else if (orderState == External.TAPI_ORDER_STATE_APPLY)
        return "APPLY";
      else
        return "INVALID";
    }

    [ExcelFunction(Category = "TradingPlatform", Name = "TP.ESunnyGlobal.SendOrder", Description = "Send order")]
    public static object SendOrder(string exchange, string commodityNo, string contractNo,
      bool isMarketOrder, bool isBuyOrder, double price, int quantity)
    {
      if (ExcelDnaUtil.IsInFunctionWizard()) return "#ERR: In function wizard.";

      if (APIInstance.api == null) return "#ERR: API not ready yet.";

      var newOrder = new TapAPINewOrder
      {
        ClientID = Settings.Default.ESunny_Global_Trade_UserNo,
        AccountNo = Settings.Default.ESunny_Global_Trade_UserNo,
        ExchangeNo = exchange,
        CommodityType = contractNo.Contains("/") ? External.TAPI_COMMODITY_TYPE_SPREAD_MONTH
              : External.TAPI_COMMODITY_TYPE_FUTURES,
        CommodityNo = commodityNo,
        ContractNo = contractNo.Contains("/") ? contractNo.Split('/')[0] : contractNo,
        ContractNo2 = contractNo.Contains("/") ? contractNo.Split('/')[1] : "",
        OrderType = isMarketOrder ? External.TAPI_ORDER_TYPE_MARKET : External.TAPI_ORDER_TYPE_LIMIT,
        OrderSource = External.TAPI_ORDER_SOURCE_PROGRAM,
        TimeInForce = External.TAPI_ORDER_TIMEINFORCE_GFD,
        OrderSide = isBuyOrder ? External.TAPI_SIDE_BUY : External.TAPI_SIDE_SELL,
        OrderPrice = price,
        OrderQty = (uint)Math.Abs(quantity)
      };

      if (!sendOrderEventHanldersInitialized)
      {
        APIInstance.callback.OnRspOrderActionEvent += OnRspOrderActionHandler;
        APIInstance.callback.OnRtnOrderEvent += OnRtnOrderHandler;

        sendOrderEventHanldersInitialized = true;
      }

      var errorCode = APIInstance.api.InsertOrder(out uint sessionId, out string clientOrderNo, newOrder);

      if (errorCode != External.TAPIERROR_SUCCEED)
        return "#ERR: Failed to send order: error code is " + errorCode;
      else
          return "Success!";
    }

    [ExcelFunction(Category = "TradingPlatform", Name = "TP.ESunnyGlobal.CancelOrder", Description = "Cancel order")]
    public static object CancelOrder(string orderNo)
    {
      if (ExcelDnaUtil.IsInFunctionWizard()) return "#ERR: In function wizard.";

      if (APIInstance.api == null) return "#ERR: API not ready yet.";

      var orderCancelReq = new TapAPIOrderCancelReq
      {
        OrderNo = orderNo
      };

      var errorCode = APIInstance.api.CancelOrder(out uint sessionId, orderCancelReq);

      if (errorCode == External.TAPIERROR_SUCCEED)
        return "Success!";
      else
        return "#ERR: Failed to cancel order: error code is " + errorCode;
    }

    [ExcelFunction(Category = "TradingPlatform", Name = "TP.ESunnyGlobal.QueryOrders", Description = "Query orders")]
    public static object QueryOrders()
    {
      if (ExcelDnaUtil.IsInFunctionWizard()) return "#ERR: In function wizard.";

      if (APIInstance.api == null) return "#ERR: API not ready yet.";

      lock (APIInstance.orderInfoMap)
      {
        var result = new object[APIInstance.orderInfoMap.Count + 1, 12];

        result[0, 0] = "OrderNo";
        result[0, 1] = "Exchange";
        result[0, 2] = "Commodity";
        result[0, 3] = "Contract";
        result[0, 4] = "Type";
        result[0, 5] = "Side";
        result[0, 6] = "Price";
        result[0, 7] = "Quantity";
        result[0, 8] = "State";
        result[0, 9] = "PlacedTime";
        result[0, 10] = "UpdatedTime";
        result[0, 11] = "FilledQty";

        var i = 0;
        foreach (var item in APIInstance.orderInfoMap)
        {
          var orderInfo = item.Value;
          i++;

          var isMarketOrder = (orderInfo.Item4 == External.TAPI_ORDER_TYPE_MARKET);
          var isBuySide = (orderInfo.Item5 == External.TAPI_SIDE_BUY);

          result[i, 0] = item.Key;
          result[i, 1] = orderInfo.Item1;
          result[i, 2] = orderInfo.Item2;
          result[i, 3] = orderInfo.Item3;
          result[i, 4] = (isMarketOrder ? "Market" : "Limit");
          result[i, 5] = (isBuySide ? "Buy" : "Sell");
          result[i, 6] = (isMarketOrder ? 0.0 : orderInfo.Item6);
          result[i, 7] = orderInfo.Item7;
          result[i, 8] = TranslateOrderState(orderInfo.Rest.Item1);
          result[i, 9] = orderInfo.Rest.Item2;
          result[i, 10] = orderInfo.Rest.Item3;
          result[i, 11] = orderInfo.Rest.Item4;
        }

        return result;
      }
    }

    [ExcelFunction(Category = "TradingPlatform", Name = "TP.ESunnyGlobal.QueryFills", Description = "Query order fills")]
    public static object QueryFills()
    {
      if (ExcelDnaUtil.IsInFunctionWizard()) return "#ERR: In function wizard.";

      if (APIInstance.api == null) return "#ERR: API not ready yet.";

      lock (APIInstance.fillInfoMap)
      {
        var result = new object[APIInstance.fillInfoMap.Count + 1, 9];

        result[0, 0] = "SeqNo";
        result[0, 1] = "OrderNo";
        result[0, 2] = "Exchange";
        result[0, 3] = "Commodity";
        result[0, 4] = "Contract";
        result[0, 5] = "Side";
        result[0, 6] = "Price";
        result[0, 7] = "Quantity";
        result[0, 8] = "Timestamp";

        var i = 0;
        foreach (var item in APIInstance.fillInfoMap)
        {
          var fillInfo = item.Value;
          i++;

          var isBuySide = (fillInfo.Item5 == External.TAPI_SIDE_BUY);

          result[i, 0] = i;
          result[i, 1] = fillInfo.Item1;
          result[i, 2] = fillInfo.Item2;
          result[i, 3] = fillInfo.Item3;
          result[i, 4] = fillInfo.Item4;
          result[i, 5] = (isBuySide ? "Buy" : "Sell");
          result[i, 6] = fillInfo.Item6;
          result[i, 7] = fillInfo.Item7;
          result[i, 8] = fillInfo.Rest.Item1;
        }

        return result;
      }
    }

    [ExcelFunction(Category = "TradingPlatform", Name = "TP.ESunnyGlobal.PositionSummary", Description = "Position Summary")]
    public static object PositionSummary()
    {
      if (ExcelDnaUtil.IsInFunctionWizard()) return "#ERR: In function wizard.";

      if (APIInstance.api == null) return "#ERR: API not ready yet.";

      lock (APIInstance.positionMap)
      {
        var result = new object[APIInstance.positionMap.Count + 1, 4];

        result[0, 0] = "Exchange";
        result[0, 1] = "Commodity";
        result[0, 2] = "Contract";
        result[0, 3] = "Quantity";

        var i = 0;
        foreach (var item in APIInstance.positionMap)
        {
          i++;

          result[i, 0] = item.Key.Item1;
          result[i, 1] = item.Key.Item2;
          result[i, 2] = item.Key.Item3;
          result[i, 3] = item.Value;
        }

        return result;
      }
    }
  }
}
