%module(directors="1") External
%include <arrays_csharp.i>

%{
#include <DataCollect.h>
#include <ThostFtdcUserApiStruct.h>
#include <ThostFtdcUserApiDataType.h>
#include <ThostFtdcMdApi.h>
#include <ThostFtdcTraderApi.h>
%}

// These symbols are NEVER used in original files
%ignore TThostFtdcVirementTradeCodeType;
%ignore THOST_FTDC_VTC_BankBankToFuture;
%ignore THOST_FTDC_VTC_BankFutureToBank;
%ignore THOST_FTDC_VTC_FutureBankToFuture;
%ignore THOST_FTDC_VTC_FutureFutureToBank;
%ignore TThostFtdcFBTTradeCodeEnumType;
%ignore THOST_FTDC_FTC_BankLaunchBankToBroker;
%ignore THOST_FTDC_FTC_BrokerLaunchBankToBroker;
%ignore THOST_FTDC_FTC_BankLaunchBrokerToBank;
%ignore THOST_FTDC_FTC_BrokerLaunchBrokerToBank;

// Typemap for premitive out parameters
%define %DefineOutParams(TYPE1, TYPE2)
    %typemap(cstype) TYPE1 & "out TYPE2"
    %typemap(imtype) TYPE1 & "out TYPE2"
    %typemap(csin) TYPE1 & %{out $csinput%}

    %typemap(cstype) TYPE1 * "out TYPE2"
    %typemap(imtype) TYPE1 * "out TYPE2"
    %typemap(csin) TYPE1 * %{out $csinput%}
%enddef
%DefineOutParams(int, int)

// Typemap for c-string array
CSHARP_ARRAYS(char *, string)
%typemap(imtype, inattributes="[global::System.Runtime.InteropServices.In, global::System.Runtime.InteropServices.MarshalAs(global::System.Runtime.InteropServices.UnmanagedType.LPArray, SizeParamIndex=0, ArraySubType=global::System.Runtime.InteropServices.UnmanagedType.LPStr)]") char *INPUT[] "string[]"
%apply char *INPUT[] { char *ppInstrumentID[] };

%feature("director") CThostFtdcMdSpi;
%feature("director") CThostFtdcTraderSpi;

%include <DataCollect.h>
%include <ThostFtdcUserApiStruct.h>
%include <ThostFtdcUserApiDataType.h>
%include <ThostFtdcMdApi.h>
%include <ThostFtdcTraderApi.h>
