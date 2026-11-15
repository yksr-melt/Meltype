// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
#include <stdint.h>
uint64_t meltype_ios_create(void);
void meltype_ios_destroy(uint64_t);
char *meltype_ios_key(uint64_t, int32_t, int32_t, int32_t);
char *meltype_ios_select(uint64_t, uint32_t);
char *meltype_ios_commit(uint64_t);
char *meltype_ios_reset(uint64_t);
void meltype_ios_free(char *);

char *meltype_ios_reconvert(uint64_t, const char *, uint32_t);

int32_t meltype_ios_set_ai(uint64_t, const char *, uint32_t, int32_t);
int32_t meltype_ios_set_currency(uint64_t, const char *, uint32_t, const char *, uint32_t, int32_t);
