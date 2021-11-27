%module(directors="1") External
%include <windows.i>

%{
#include <TapDataCollectAPI.h>
#include <TapAPICommDef.h>
#include <TapAPIError.h>
#include <TapTradeAPIDataType.h>
#include <TapTradeAPI.h>
%}

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
%DefineOutParams(unsigned int, uint)

%feature("director") ITapTradeAPINotify;

%include <TapDataCollectAPI.h>
%include <TapAPICommDef.h>
%include <TapAPIError.h>
%include <TapTradeAPIDataType.h>
%include <TapTradeAPI.h>
