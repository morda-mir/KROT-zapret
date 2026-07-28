-- KROT wrapper for zapret2 automatic UDP strategy selection.
-- Reports a strategy only after incoming traffic confirms that it works.

local krot_reported = {}

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
		if hrec and hrec.nstrategy then
			local crec = automate_conn_record(desync)
			if standard_success_detector(desync, crec) then
				local channel = desync.arg.channel
				local strategy_id = krot_strategy_id(
					desync.arg.ids,
					hrec.nstrategy)
				if channel
					and string.match(channel, "^[a-z_]+$")
					and strategy_id
					and string.match(strategy_id, "^[a-z0-9%-]+$")
					and krot_reported[channel] ~= strategy_id then
					krot_reported[channel] = strategy_id
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
