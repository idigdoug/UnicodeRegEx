#include "pch.h"
#include "InputValidation.h"

bool
InputIsValid(RegExBytes input) noexcept
{
    // size must be non-negative: it is cast to size_t and used as the buffer
    // length, so a negative size would become a huge bound and drive an
    // out-of-bounds read.
    if (input.size < 0)
    {
        return false;
    }

    // A non-empty buffer must have a non-null base. Do NOT otherwise validate
    // data: a pointer is a bag of bits. High bits are legitimately set (high
    // user-mode addresses on 32-bit; canonical/tagged forms on 64-bit), and on
    // 32-bit a high pointer cast through nint sign-extends to a negative
    // LONGLONG even though its UINT_PTR value is fine -- so any signedness or
    // magnitude check on data would wrongly reject valid input.
    if (input.size != 0 && input.data == 0)
    {
        return false;
    }

    return true;
}

bool
RangeIsInBounds(LONGLONG offset, LONGLONG size, size_t bufferSize) noexcept
{
    auto const offsetU = static_cast<size_t>(offset);
    auto const sizeU = static_cast<size_t>(size);

    if (offsetU > bufferSize || sizeU > bufferSize - offsetU)
    {
        return false;
    }

    return true;
}

bool
InputIsAligned(TextEncoding encoding, LONGLONG lowBits) noexcept
{
    return std::visit([lowBits](auto enc)
        {
            using CharT = typename decltype(enc)::encoded_char;
            return (lowBits & (sizeof(CharT) - 1)) == 0;
        },
        encoding);
}

_Success_(return)
bool
TextEncodingForCodePageIfAligned(unsigned codePage, LONGLONG lowBits, _Out_ TextEncoding* encoding) noexcept
{
    return VisitEncodingForCodePage(codePage, [&](auto enc) {
        using EncodingT = decltype(enc);
        *encoding = enc;
        return (lowBits & (sizeof(typename EncodingT::encoded_char) - 1)) == 0;
        });
}
