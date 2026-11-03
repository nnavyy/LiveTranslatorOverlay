"""Allow an empty transcript while keeping Whisper's other token suppressions."""
def allow_empty_caption(model):
    config = model.generation_config
    config.begin_suppress_tokens = [token for token in (config.begin_suppress_tokens or [])
                                    if token != config.eos_token_id]
