#include "pch.h"
#include <TextEncoding.h>

std::optional<Sbcs>
Sbcs::TryFromCodePage(unsigned codePage) noexcept
{
    static std::unordered_map<unsigned, Table> tables;
    static wil::srwlock tablesMutex;

    {
        auto lock = tablesMutex.lock_shared();
        auto it = tables.find(codePage);
        if (it != tables.end())
        {
            return Sbcs(&it->second);
        }
    }

    CPINFO info{};
    GetCPInfo(codePage, &info);
    if (info.MaxCharSize != 1)
    {
        return std::nullopt;
    }

    try
    {
        char narrow[256];
        for (unsigned i = 0; i < 256; i++)
        {
            narrow[i] = static_cast<char>(i);
        }

        auto lock = tablesMutex.lock_exclusive();
        auto [it, inserted] = tables.try_emplace(codePage);
        if (inserted)
        {
            int result = MultiByteToWideChar(
                codePage,
                0,
                narrow,
                256,
                it->second.Values,
                256);
            if (result != 256)
            {
                tables.erase(it);
                return std::nullopt;
            }

            it->second.CodePage = codePage;
            it->second.DefaultChar = info.DefaultChar[0] ? info.DefaultChar[0] : '?';
        }

        return Sbcs(&it->second);
    }
    catch (std::bad_alloc const&)
    {
    }

    return std::nullopt;
}

std::span<Sbcs::encoded_char>
Sbcs::ConvertInPlace(std::span<char32_t> codePoints) const noexcept
{
    auto const pWide = reinterpret_cast<wchar_t*>(codePoints.data());
    auto const pSbcs = reinterpret_cast<encoded_char*>(pWide + codePoints.size());

    // Convert utf-32 in whole buffer to utf-16 in first half of buffer.
    // SBCS is always BMP, so reject non-BMP. Surrogates are always errors.
    for (size_t i = 0; i != codePoints.size(); i += 1)
    {
        auto const ch = codePoints[i];
        pWide[i] = CodePoint::IsScalarBmp(ch)
            ? static_cast<wchar_t>(ch)
            : static_cast<wchar_t>(CodePoint::ReplacementChar);
    }

    // Convert utf-16 in first half of buffer to SBCS in second half of buffer.
    for (size_t pos = 0; pos != codePoints.size();)
    {
        auto const BatchMax = 0x10000000; // Hard limit is INT_MAX.
        auto const remaining = codePoints.size() - pos;
        auto const batchSize = remaining > BatchMax ? BatchMax : static_cast<int>(remaining);
        auto const converted = WideCharToMultiByte(CodePage(), 0, pWide + pos, batchSize, pSbcs + pos, batchSize, nullptr, nullptr);
        if (converted != batchSize)
        {
            memset(pSbcs + pos, m_table->DefaultChar, batchSize);
        }

        pos += batchSize;
    }

    return std::span{ pSbcs, codePoints.size() };
}
