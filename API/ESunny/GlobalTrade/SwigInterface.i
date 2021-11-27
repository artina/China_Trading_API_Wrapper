%module(directors="1") External
%include <windows.i>

%{
#include <iTapAPICommDef.h>
#include <iTapAPIError.h>
#include <iTapTradeAPIDataType.h>
#include <iTapTradeAPI.h>
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

// Typemap for string out parameters
%define %DefineOutParams_String(DIM)
    %typemap(ctype) char (*) [DIM] "char **"
    %typemap(imtype) char (*) [DIM] "out string"
    %typemap(cstype) char (*) [DIM] "out string"
    %typemap(csin) char (*) [DIM] %{out $csinput%}

    %typemap(argout) char (*) [DIM]
    %{
        *$input = SWIG_csharp_string_callback(*$1);
    %}
%enddef
%DefineOutParams_String(51)

%feature("director") ITapTradeAPINotify;

%include <iTapAPICommDef.h>
%include <iTapAPIError.h>
%include <iTapTradeAPIDataType.h>
%include <iTapTradeAPI.h>
