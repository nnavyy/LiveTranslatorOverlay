"""Independent language selection; preserve successful languages and old artifacts."""
DISTIL_REPO = 'distil-whisper/distil-large-v3'
DISTIL_REVISION = '8031d2e6ce6631b7fc45629dddfc00271116d981'
RUSSIAN_MODEL = 'v3_rnnt'
RUSSIAN_MD5 = '0fd2c9a1ff66abd8d32a3a07f7592815'


def select_language(language, baseline, candidates):
    ref=baseline['speech'][language]
    eligible={name:(score['speech'][language]['wer'] <= ref['wer']
        and score['speech'][language]['falseRejections'] <= ref['falseRejections']
        and score['nonSpeech']['emittedClips'] <= baseline['nonSpeech']['emittedClips'])
        for name,score in candidates.items()}
    # Dict order is the predeclared tie preference, with the current system first.
    pool=[name for name in candidates if eligible[name]]
    if not pool:
        raise ValueError('The current baseline must be present and eligible')
    selected=min(pool,key=lambda name:candidates[name]['speech'][language]['wer'])
    return dict(selected=selected,eligibility=eligible,
        improved=candidates[selected]['speech'][language]['wer']<ref['wer'])


def targets(score):
    ru,en=(score['speech'][lang]['wer'] for lang in ('ru','en'))
    return dict(russianAtMost4_70=ru<=.047,englishAtMost2_67=en<=.0267,
        macroMeanWer=(ru+en)/2,meanAtMost3=(ru+en)/2<=.03,bothAtMost2=ru<=.02 and en<=.02)
