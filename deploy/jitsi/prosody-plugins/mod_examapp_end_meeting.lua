-- Issue #490: HTTP endpoint that destroys a meeting room, used by the ExamApp API to end a lesson server-side
-- (booking cancelled, teacher suspended).
--
-- Copy of the stock jitsi mod_muc_end_meeting (jitsi/prosody:stable-9584, Copyright (C) 2023-present 8x8, Inc.,
-- Apache-2.0) with one change: the stock module always calls token_util:set_asap_key_server(...) with the option's
-- default "" which is truthy in Lua, so it only accepts ASAP (RS256 + key server) tokens. ExamApp signs every Jitsi
-- token with the shared JWT_APP_SECRET (HS256), so this copy keeps token_util on the main host's app_secret.
--
-- POST /end-meeting?conference=<room>@<muc domain>, header "Authorization: Bearer <jwt>".
-- The path is served on prosody's internal HTTP port only; jitsi-web's nginx does not proxy it.

module:set_global();

local util = module:require "util";
local async_handler_wrapper = util.async_handler_wrapper;
local room_jid_match_rewrite = util.room_jid_match_rewrite;
local get_room_from_jid = util.get_room_from_jid;
local starts_with = util.starts_with;

local neturl = require "net.url";
local parse = neturl.parseQuery;

-- initialized once the main virtual host module is added
local token_util;

local muc_domain_base = module:get_option_string("muc_mapper_domain_base");

local function verify_token(token)
    if token == nil then
        module:log("warn", "no token provided");
        return false;
    end

    local session = {};
    session.auth_token = token;
    local verified, reason, msg = token_util:process_and_verify_token(session);
    if not verified then
        module:log("warn", "not a valid token %s %s", tostring(reason), tostring(msg));
        return false;
    end
    return true;
end

local function handle_terminate_meeting(event)
    if not event.request.url.query then
        return { status_code = 400 };
    end
    local params = parse(event.request.url.query);
    local conference = params["conference"];
    if not conference then
        module:log("warn", "conference param was not provided");
        return { status_code = 400 };
    end
    local room_jid = room_jid_match_rewrite(conference);

    local token = event.request.headers["authorization"];
    if not token or not starts_with(token, "Bearer ") then
        module:log("warn", "Authorization header missing or invalid for conference %s", conference);
        return { status_code = 401 };
    end
    token = token:sub(8, #token);

    if not verify_token(token) then
        return { status_code = 401 };
    end

    local room = get_room_from_jid(room_jid);
    if not room then
        return { status_code = 404 };
    end

    module:log("info", "Destroy room jid %s", room.jid);
    room:destroy(nil, "The meeting has been terminated");
    return { status_code = 200 };
end

function module.add_host(host_module)
    if host_module.host == muc_domain_base then
        token_util = module:require "token/util".new(host_module);

        host_module:depends("http");
        host_module:provides("http", {
            default_path = "/";
            route = {
                ["POST end-meeting"] = function(event)
                    return async_handler_wrapper(event, handle_terminate_meeting)
                end;
            };
        });
    end
end
