%module(directors="1") External
%include <windows.i>

%{
#include <TapAPICommDef.h>
#include <TapAPIError.h>
#include <TapQuoteAPIDataType.h>
#include <TapQuoteAPI.h>
%}

%include <carrays.i>
%array_functions(double, ArrayUtils_double);
%array_functions(unsigned long long, ArrayUtils_ulong);

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

%feature("director") ITapQuoteAPINotify;

%include <TapAPICommDef.h>
%include <TapAPIError.h>
%include <TapQuoteAPIDataType.h>
%include <TapQuoteAPI.h>
