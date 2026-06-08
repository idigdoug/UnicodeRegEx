#include "pch.h"
#include <RepStrRegEx.h>

// RegExSyntaxFlags
static_assert(RegExSyntaxFlags_perl_syntax_group == (int)boost::regbase::perl_syntax_group);
static_assert(RegExSyntaxFlags_basic_syntax_group == (int)boost::regbase::basic_syntax_group);
static_assert(RegExSyntaxFlags_literal == (int)boost::regbase::literal);
static_assert(RegExSyntaxFlags_syntax_group_mask == (int)boost::regbase::main_option_type);
static_assert(RegExSyntaxFlags_icase == (int)boost::regex_constants::icase);
static_assert(RegExSyntaxFlags_nosubs == (int)boost::regex_constants::nosubs);
static_assert(RegExSyntaxFlags_optimize == (int)boost::regex_constants::optimize);
static_assert(RegExSyntaxFlags_collate == (int)boost::regex_constants::collate);
static_assert(RegExSyntaxFlags_normal == (int)boost::regex_constants::normal);
static_assert(RegExSyntaxFlags_perl == (int)boost::regex_constants::perl);
static_assert(RegExSyntaxFlags_ECMAScript == (int)boost::regex_constants::ECMAScript);
static_assert(RegExSyntaxFlags_basic == (int)boost::regex_constants::basic);
static_assert(RegExSyntaxFlags_extended == (int)boost::regex_constants::extended);
static_assert(RegExSyntaxFlags_awk == (int)boost::regex_constants::awk);
static_assert(RegExSyntaxFlags_grep == (int)boost::regex_constants::grep);
static_assert(RegExSyntaxFlags_egrep == (int)boost::regex_constants::egrep);

// RegExMatchFlags
static_assert(RegExMatchFlag_default == (int)boost::regex_constants::match_default);
static_assert(RegExMatchFlag_not_bol == (int)boost::regex_constants::match_not_bol);
static_assert(RegExMatchFlag_not_eol == (int)boost::regex_constants::match_not_eol);
static_assert(RegExMatchFlag_not_bow == (int)boost::regex_constants::match_not_bow);
static_assert(RegExMatchFlag_not_eow == (int)boost::regex_constants::match_not_eow);
static_assert(RegExMatchFlag_any == (int)boost::regex_constants::match_any);
static_assert(RegExMatchFlag_not_null == (int)boost::regex_constants::match_not_null);
static_assert(RegExMatchFlag_continuous == (int)boost::regex_constants::match_continuous);

// RegExFormatFlags
static_assert(RegExFormatFlag_perl == (int)boost::regex_constants::format_perl);
static_assert(RegExFormatFlag_sed == (int)boost::regex_constants::format_sed);
static_assert(RegExFormatFlag_boost_extensions == (int)boost::regex_constants::format_all);
static_assert(RegExFormatFlag_no_copy == (int)boost::regex_constants::format_no_copy);
static_assert(RegExFormatFlag_first_only == (int)boost::regex_constants::format_first_only);

// RegExErrorCode
static_assert(RegExErrorCode_ok == (int)boost::regex_constants::error_ok);
static_assert(RegExErrorCode_no_match == (int)boost::regex_constants::error_no_match);
static_assert(RegExErrorCode_bad_pattern == (int)boost::regex_constants::error_bad_pattern);
static_assert(RegExErrorCode_collate == (int)boost::regex_constants::error_collate);
static_assert(RegExErrorCode_ctype == (int)boost::regex_constants::error_ctype);
static_assert(RegExErrorCode_escape == (int)boost::regex_constants::error_escape);
static_assert(RegExErrorCode_backref == (int)boost::regex_constants::error_backref);
static_assert(RegExErrorCode_brack == (int)boost::regex_constants::error_brack);
static_assert(RegExErrorCode_paren == (int)boost::regex_constants::error_paren);
static_assert(RegExErrorCode_brace == (int)boost::regex_constants::error_brace);
static_assert(RegExErrorCode_badbrace == (int)boost::regex_constants::error_badbrace);
static_assert(RegExErrorCode_range == (int)boost::regex_constants::error_range);
static_assert(RegExErrorCode_space == (int)boost::regex_constants::error_space);
static_assert(RegExErrorCode_badrepeat == (int)boost::regex_constants::error_badrepeat);
static_assert(RegExErrorCode_end == (int)boost::regex_constants::error_end);
static_assert(RegExErrorCode_size == (int)boost::regex_constants::error_size);
static_assert(RegExErrorCode_right_paren == (int)boost::regex_constants::error_right_paren);
static_assert(RegExErrorCode_empty == (int)boost::regex_constants::error_empty);
static_assert(RegExErrorCode_complexity == (int)boost::regex_constants::error_complexity);
static_assert(RegExErrorCode_stack == (int)boost::regex_constants::error_stack);
static_assert(RegExErrorCode_perl_extension == (int)boost::regex_constants::error_perl_extension);
static_assert(RegExErrorCode_unknown == (int)boost::regex_constants::error_unknown);
