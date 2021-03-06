﻿using System;
using System.Collections.Generic;
using System.Linq;
using StarryEyes.Filters.Expressions;
using StarryEyes.Filters.Expressions.Operators;
using StarryEyes.Filters.Expressions.Values;
using StarryEyes.Filters.Expressions.Values.Immediates;
using StarryEyes.Filters.Expressions.Values.Locals;
using StarryEyes.Filters.Expressions.Values.Statuses;
using StarryEyes.Filters.Expressions.Values.Users;
using StarryEyes.Filters.Sources;

namespace StarryEyes.Filters.Parsing
{
    public static class QueryCompiler
    {
        public static FilterQuery Compile(string query)
        {
            try
            {
                var tokens = Tokenizer.Tokenize(query).ToArray();
                // (from (sources)) (where (filters))
                var first = tokens.FirstOrDefault();
                if (first.IsMatchTokenLiteral("from"))
                {
                    // compile with sources && predicates
                    var sourceSequence = tokens.Skip(1).TakeWhile(t => !t.IsMatchTokenLiteral("where"));
                    var sources = CompileSources(sourceSequence).ToArray();
                    var predicateSequence = tokens.SkipWhile(t => !t.IsMatchTokenLiteral("where")).Skip(1).ToArray();
                    if (predicateSequence.Length == 0)
                    {
                        // without predicates
                        return new FilterQuery { Sources = sources, PredicateTreeRoot = new FilterExpressionRoot() };
                    }
                    var filters = CompileFilters(predicateSequence);
                    return new FilterQuery { Sources = sources, PredicateTreeRoot = filters };
                }
                if (first.IsMatchTokenLiteral("where"))
                {
                    // compile without sources
                    var sources = new FilterSourceBase[] { new FilterLocal() }; // implicit "from all"
                    var filters = CompileFilters(tokens.Skip(1));
                    return new FilterQuery { Sources = sources, PredicateTreeRoot = filters };
                }
                throw new FormatException("クエリは\"from\"キーワードか\"where\"キーワードで始まらなければなりません。");
            }
            catch (FilterQueryException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new FilterQueryException("クエリのコンパイルに失敗しました。 " + ex.Message, query, ex);
            }
        }

        public static FilterExpressionRoot CompileFilters(string query)
        {
            try
            {
                var tokens = Tokenizer.Tokenize(query);
                // from (sources) where (filters)
                return CompileFilters(tokens);
            }
            catch (FilterQueryException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new FilterQueryException("クエリのコンパイルに失敗しました: " + ex.Message, query, ex);
            }
        }

        #region sources compiler

        private static readonly IDictionary<string, Type> FilterSourceResolver = new SortedDictionary<string, Type>
        {
            {"*", typeof (FilterLocal)},
            {"local", typeof (FilterLocal)},
            {"all", typeof (FilterLocal)},
            {"home", typeof (FilterHome)},
            {"list", typeof (FilterList)},
            {"mention", typeof (FilterMentions)},
            {"mentions", typeof (FilterMentions)},
            {"reply", typeof (FilterMentions)},
            {"replies", typeof (FilterMentions)},
            {"message", typeof (FilterMessages)},
            {"messages", typeof (FilterMessages)},
            {"dm", typeof (FilterMessages)},
            {"dms", typeof (FilterMessages)},
            {"search", typeof (FilterSearch)},
            {"find", typeof (FilterSearch)},
            {"track", typeof (FilterTrack)},
            {"stream", typeof (FilterTrack)},
        };

        /// <summary>
        ///     Instantiate sources from tokens.
        /// </summary>
        private static IEnumerable<FilterSourceBase> CompileSources(IEnumerable<Token> token)
        {
            // filter
            // filter: "argument"
            // filter: "argument1", "argument2", ... -> filter: "argument1", filter: "argument2", ...
            var reader = new TokenReader(token);
            while (reader.IsRemainToken)
            {
                var filter = reader.Get();
                if (filter.Type != TokenType.Literal && filter.Type != TokenType.OperatorMultiple)
                {
                    throw new ArgumentException("このトークンは無効です: " + filter.Type +
                                                " (リテラルか \'*\' です。)");
                }
                Type fstype;
                if (!FilterSourceResolver.TryGetValue(filter.Value, out fstype))
                    throw new ArgumentException("フィルタ ソースが一致しません: " + filter.Value);
                if (reader.IsRemainToken && reader.LookAhead().Type == TokenType.Collon) // with argument
                {
                    reader.AssertGet(TokenType.Collon);
                    do
                    {
                        var argument = reader.AssertGet(TokenType.String);
                        yield return Activator.CreateInstance(fstype, argument.Value) as FilterSourceBase;
                        // separated by comma
                        if (reader.IsRemainToken)
                        {
                            reader.AssertGet(TokenType.Comma);
                        }
                    } while (reader.IsRemainToken && reader.LookAhead().Type == TokenType.String);
                }
                else
                {
                    yield return Activator.CreateInstance(fstype) as FilterSourceBase;
                    if (reader.IsRemainToken)
                    {
                        // filters are divided by comma
                        reader.AssertGet(TokenType.Comma);
                    }
                }
            }
        }

        #endregion

        #region filter expression tree compiler

        /// <summary>
        ///     Instantiate expression tree from tokens.
        /// </summary>
        private static FilterExpressionRoot CompileFilters(IEnumerable<Token> token)
        {
            var reader = new TokenReader(token);
            var op = CompileL0(reader);
            if (reader.IsRemainToken)
                throw new FilterQueryException("不正なトークンです: " + reader.Get(), reader.RemainQuery);
            return new FilterExpressionRoot { Operator = op };
        }

        // Operators:
        // All: + - * / | & <- -> == > >= < <= != ! in contains
        // L0: |
        // L1: &
        // L2: == !=
        // L3: < <= > >=
        // L4: <- -> in contains
        // L5: + -
        // L6: * /
        // L7: !
        // L8: value (or in bracket, return L0)

        private static FilterOperatorBase CompileL0(TokenReader reader)
        {
            // |
            var left = CompileL1(reader);
            if (!reader.IsRemainToken)
                return left;
            var generate = (Func<TokenType, FilterTwoValueOperator, FilterOperatorBase>)
                           ((type, oper) => GenerateSink(reader, left, type, oper, CompileL0));
            switch (reader.LookAhead().Type)
            {
                case TokenType.OperatorOr:
                    return generate(TokenType.OperatorOr, new FilterOperatorOr());
                default:
                    return left;
            }
        }

        private static FilterOperatorBase CompileL1(TokenReader reader)
        {
            // &
            var left = CompileL2(reader);
            if (!reader.IsRemainToken)
                return left;
            var generate = (Func<TokenType, FilterTwoValueOperator, FilterOperatorBase>)
                           ((type, oper) => GenerateSink(reader, left, type, oper, CompileL1));
            switch (reader.LookAhead().Type)
            {
                case TokenType.OperatorAnd:
                    return generate(TokenType.OperatorAnd, new FilterOperatorAnd());
                default:
                    return left;
            }
        }

        private static FilterOperatorBase CompileL2(TokenReader reader)
        {
            // == !=
            var left = CompileL3(reader);
            if (!reader.IsRemainToken)
                return left;
            var generate = (Func<TokenType, FilterTwoValueOperator, FilterOperatorBase>)
                           ((type, oper) => GenerateSink(reader, left, type, oper, CompileL2));
            switch (reader.LookAhead().Type)
            {
                case TokenType.OperatorEquals:
                    return generate(TokenType.OperatorEquals, new FilterOperatorEquals());
                case TokenType.OperatorNotEquals:
                    return generate(TokenType.OperatorNotEquals, new FilterOperatorNotEquals());
                default:
                    return left;
            }
        }

        private static FilterOperatorBase CompileL3(TokenReader reader)
        {
            // < <= > >=
            var left = CompileL4(reader);
            if (!reader.IsRemainToken)
                return left;
            var generate = (Func<TokenType, FilterTwoValueOperator, FilterOperatorBase>)
                           ((type, oper) => GenerateSink(reader, left, type, oper, CompileL3));
            switch (reader.LookAhead().Type)
            {
                case TokenType.OperatorLessThan:
                    return generate(TokenType.OperatorLessThan, new FilterOperatorLessThan());
                case TokenType.OperatorLessThanOrEqual:
                    return generate(TokenType.OperatorLessThanOrEqual, new FilterOperatorLessThanOrEqual());
                case TokenType.OperatorGreaterThan:
                    return generate(TokenType.OperatorGreaterThan, new FilterOperatorGreaterThan());
                case TokenType.OperatorGreaterThanOrEqual:
                    return generate(TokenType.OperatorGreaterThanOrEqual, new FilterOperatorGreaterThanOrEqual());
                default:
                    return left;
            }
        }

        private static FilterOperatorBase CompileL4(TokenReader reader)
        {
            // <- -> in contains
            var left = CompileL5(reader);
            if (!reader.IsRemainToken)
                return left;
            var generate = (Func<TokenType, FilterTwoValueOperator, FilterOperatorBase>)
                           ((type, oper) => GenerateSink(reader, left, type, oper, CompileL4));
            switch (reader.LookAhead().Type)
            {
                case TokenType.OperatorContains:
                    return generate(TokenType.OperatorContains, new FilterOperatorContains());
                case TokenType.OperatorContainedBy:
                    return generate(TokenType.OperatorContainedBy, new FilterOperatorContainedBy());
                case TokenType.Literal:
                    switch (reader.LookAhead().Value.ToLower())
                    {
                        case "in":
                            // <-
                            return generate(TokenType.Literal, new FilterOperatorContainedBy());
                        case "contains":
                            // ->
                            return generate(TokenType.Literal, new FilterOperatorContains());
                        default:
                            return left;
                    }
                default:
                    return left;
            }
        }

        private static FilterOperatorBase CompileL5(TokenReader reader)
        {
            // parse arithmetic operators
            var left = CompileL6(reader);
            if (!reader.IsRemainToken)
                return left;
            var generate = (Func<TokenType, FilterTwoValueOperator, FilterOperatorBase>)
                           ((type, oper) => GenerateSink(reader, left, type, oper, CompileL5));
            switch (reader.LookAhead().Type)
            {
                case TokenType.OperatorPlus:
                    return generate(TokenType.OperatorPlus, new FilterOperatorPlus());
                case TokenType.OperatorMinus:
                    return generate(TokenType.OperatorMinus, new FilterOperatorMinus());
                default:
                    return left;
            }
        }

        private static FilterOperatorBase CompileL6(TokenReader reader)
        {
            // parse arithmetic operators (faster)
            var left = CompileL7(reader);
            if (!reader.IsRemainToken)
                return left;
            var generate = (Func<TokenType, FilterTwoValueOperator, FilterOperatorBase>)
                           ((type, oper) => GenerateSink(reader, left, type, oper, CompileL6));
            switch (reader.LookAhead().Type)
            {
                case TokenType.OperatorMultiple:
                    return generate(TokenType.OperatorMultiple, new FilterOperatorProduct());
                case TokenType.OperatorDivide:
                    return generate(TokenType.OperatorDivide, new FilterOperatorDivide());
                default:
                    return left;
            }
        }

        private static FilterOperatorBase CompileL7(TokenReader reader)
        {
            // parse not 
            if (reader.LookAhead().Type == TokenType.Exclamation)
            {
                reader.AssertGet(TokenType.Exclamation);
                return new FilterNegate { Value = CompileL7(reader) };
            }
            return CompileL8(reader);
        }

        private static FilterOperatorBase CompileL8(TokenReader reader)
        {
            if (reader.LookAhead().Type == TokenType.OpenBracket)
            {
                // in bracket
                reader.AssertGet(TokenType.OpenBracket);
                if (reader.LookAhead().Type == TokenType.CloseBracket)
                {
                    // empty bracket
                    reader.AssertGet(TokenType.CloseBracket);
                    return new FilterBracket(null);
                }
                var ret = CompileL0(reader);
                reader.AssertGet(TokenType.CloseBracket);
                return new FilterBracket(ret);
            }
            return GetValue(reader);
        }

        private static FilterOperatorBase GenerateSink(
            TokenReader reader,
            FilterOperatorBase leftValue,
            TokenType type,
            FilterTwoValueOperator oper,
            Func<TokenReader, FilterOperatorBase> selfCall)
        {
            reader.AssertGet(type);
            var rightValue = selfCall(reader);
            oper.LeftValue = leftValue;
            oper.RightValue = rightValue;
            return oper;
        }

        private static ValueBase GetValue(TokenReader reader)
        {
            var literal = reader.LookAhead();
            if (literal.Type == TokenType.String)
            {
                // immediate string value
                return new StringValue(reader.AssertGet(TokenType.String).Value);
            }
            if (literal.Type == TokenType.OperatorMultiple)
            {
                // for parsing asterisk user
                var pseudo = reader.AssertGet(TokenType.OperatorMultiple);
                literal = new Token(TokenType.Literal, "*", pseudo.DebugIndex);
            }
            else
            {
                literal = reader.AssertGet(TokenType.Literal);
            }
            // check first letter
            switch (literal.Value[0])
            {
                case '@':
                    // user screen name
                    return GetAccountValue(literal.Value.Substring(1), reader);
                case '#':
                    // user id
                    return GetAccountValue(literal.Value, reader);
            }
            // check first layers
            switch (literal.Value.ToLower())
            {
                case "*":
                case "we":
                case "our":
                case "us":
                    return GetAccountValue("*", reader);
                case "@":
                    reader.AssertGet(TokenType.Period);
                    return GetAccountValue(reader.AssertGet(TokenType.Literal).Value, reader);
                case "user":
                case "retweeter":
                    return GetUserValue(literal.Value == "retweeter", reader);
                default:
                    long iv;
                    if (Int64.TryParse(literal.Value, out iv))
                    {
                        return new NumericValue(iv);
                    }
                    return GetStatusValue(literal.Value, reader);
            }
        }

        private static ValueBase GetAccountValue(string value, TokenReader reader)
        {
            var repl = GetUserExpr(value);
            if (reader.IsRemainToken && reader.LookAhead().Type == TokenType.Period)
            {
                reader.AssertGet(TokenType.Period);
                var literal = reader.AssertGet(TokenType.Literal);
                switch (literal.Value.ToLower())
                {
                    case "friend":
                    case "friends":
                    case "following":
                    case "followings":
                        return new LocalUserFollowing(repl);
                    case "follower":
                    case "followers":
                        return new LocalUserFollowers(repl);
                    case "blocking":
                    case "blockings":
                        return new LocalUserBlockings(repl);
                    default:
                        throw new FilterQueryException("不正なトークンです: " + literal.Value,
                                                       repl.ToQuery() + "." + literal.Value + " " + reader.RemainQuery);
                }
            }
            return new LocalUser(repl);
        }

        private static UserExpressionBase GetUserExpr(string key)
        {
            if (key == "*")
            {
                return new UserAny();
            }
            if (key.StartsWith("#"))
            {
                var id = Int64.Parse(key.Substring(1));
                return new UserSpecified(id);
            }
            return new UserSpecified(key);
        }

        private static ValueBase GetUserValue(bool isRetweeter, TokenReader reader)
        {
            var selector = (Func<ValueBase, ValueBase, ValueBase>)
                           ((user, retweeter) => isRetweeter ? retweeter : user);
            if (reader.IsRemainToken && reader.LookAhead().Type != TokenType.Period)
            {
                // user expression
                return selector(new User(), new Retweeter());
            }
            reader.AssertGet(TokenType.Period);
            var literal = reader.AssertGet(TokenType.Literal);
            switch (literal.Value.ToLower())
            {
                case "protected":
                case "isProtected":
                case "is_protected":
                    return selector(new UserIsProtected(), new RetweeterIsProtected());
                case "verified":
                case "isVerified":
                case "is_verified":
                    return selector(new UserIsVerified(), new RetweeterIsVerified());
                case "translator":
                case "isTranslator":
                case "is_translator":
                    return selector(new UserIsTranslator(), new RetweeterIsTranslator());
                case "contributorsEnabled":
                case "contributors_enabled":
                case "isContributorsEnabled":
                case "is_contributors_enabled":
                    return selector(new UserIsContributorsEnabled(), new RetweeterIsContributorsEnabled());
                case "geoEnabled":
                case "geo_enabled":
                case "isGeoEnabled":
                case "is_geo_enabled":
                    return selector(new UserIsGeoEnabled(), new RetweeterIsGeoEnabled());
                case "id":
                    return selector(new UserId(), new RetweeterId());
                case "status":
                case "statuses":
                case "statusCount":
                case "status_count":
                case "statusesCount":
                case "statuses_count":
                    return selector(new UserStatuses(), new RetweeterStatuses());
                case "friend":
                case "friends":
                case "following":
                case "followings":
                case "friendsCount":
                case "friends_count":
                case "followingsCount":
                case "followings_count":
                    return selector(new UserFollowing(), new RetweeterFollowing());
                case "follower":
                case "followers":
                case "followersCount":
                case "followers_count":
                    return selector(new UserFollowers(), new RetweeterFollowers());
                case "fav":
                case "favCount":
                case "favorite":
                case "favorites":
                case "fav_count":
                case "favoriteCount":
                case "favorite_count":
                case "favoritesCount":
                case "favorites_count":
                    return selector(new UserFavroites(), new RetweeterFavroites());
                case "list":
                case "listed":
                case "listCount":
                case "list_count":
                case "listedCount":
                case "listed_count":
                    return selector(new UserListed(), new RetweeterListed());
                case "screenName":
                case "screen_name":
                    return selector(new UserScreenName(), new RetweeterScreenName());
                case "name":
                    return selector(new UserName(), new RetweeterName());
                case "bio":
                case "desc":
                case "description":
                    return selector(new UserDescription(), new RetweeterDescription());
                case "loc":
                case "location":
                    return selector(new UserLocation(), new RetweeterLocation());
                case "lang":
                case "language":
                    return selector(new UserLanguage(), new RetweeterLanguage());
                default:
                    throw new FilterQueryException("不正なトークンです: " + literal.Value,
                                                   "user." + literal.Value + " " + reader.RemainQuery);
            }
        }

        private static ValueBase GetStatusValue(string value, TokenReader reader)
        {
            switch (value.ToLower())
            {
                case "dm":
                case "isdm":
                case "is_dm":
                case "message":
                case "ismessage":
                case "is_message":
                case "directmessage":
                case "direct_message":
                case "isdirectmessage":
                case "is_direct_message":
                    return new StatusIsDirectMessage();
                case "rt":
                case "retweet":
                case "isretweet":
                case "is_retweet":
                    return new StatusIsRetweet();
                case "faved":
                case "favorited":
                case "isfavorited":
                case "is_favorited":
                    return new StatusIsFavorited();
                case "rted":
                case "retweeted":
                case "isretweeted":
                case "is_retweeted":
                    return new StatusIsRetweeted();
                case "mention":
                case "replyto":
                case "reply_to":
                case "inreplyto":
                case "in_reply_to":
                    return new StatusInReplyTo();
                case "to":
                    return new StatusTo();
                case "id":
                    return new StatusId();
                case "favs":
                case "favorites":
                case "favorer":
                case "favorers":
                    return new StatusFavorites();
                case "rts":
                case "retweets":
                case "retweeter":
                case "retweeters":
                    return new StatusRetweets();
                case "text":
                case "body":
                    return new StatusText();
                case "via":
                case "from":
                case "source":
                case "client":
                    return new StatusSource();
                default:
                    throw new FilterQueryException("不正なトークンです: " + value, value + " " + reader.RemainQuery);
            }
        }

        #endregion
    }
}