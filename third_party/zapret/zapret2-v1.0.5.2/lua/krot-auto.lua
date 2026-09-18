-- KROT wrapper for zapret2 automatic UDP strategy selection.
-- Reports a strategy only after incoming traffic confirms that it works.

local krot_activity_reported = setmetatable({}, { __mode = "k" })
local krot_winner_reported = setmetatable({}, { __mode = "k" })

local function krot_valid_channel(channel)
	return channel == "discord_voice" or channel == "youtube_quic"
end

local function krot_strategy_id(ids, strategy)
	local index = 1
	for value in string.gmatch(ids or "", "([^,]+)") do
		if index == strategy then
			return value
		end
		index = index + 1
	end
	return nil
end

function krot_circular(ctx, desync)
	if desync.track then
		local hrec = automate_host_record(desync)
		local crec = automate_conn_record(desync)
		local channel = desync.arg.channel
		if crec
			and krot_valid_channel(channel)
			and not krot_activity_reported[crec] then
			krot_activity_reported[crec] = true
			print("KROT_UDP_ACTIVITY|" .. channel)
		end

		if hrec and hrec.nstrategy and crec then
			if standard_success_detector(desync, crec) then
				local strategy_id = krot_strategy_id(
					desync.arg.ids,
					hrec.nstrategy)
				if krot_valid_channel(channel)
					and strategy_id
					and string.match(strategy_id, "^[a-z0-9%-]+$")
					and krot_winner_reported[crec] ~= strategy_id then
					krot_winner_reported[crec] = strategy_id
					print(
						"KROT_UDP_WINNER|"
						.. channel
						.. "|"
						.. strategy_id)
				end
			end
		end
	end

	return circular(ctx, desync)
end
