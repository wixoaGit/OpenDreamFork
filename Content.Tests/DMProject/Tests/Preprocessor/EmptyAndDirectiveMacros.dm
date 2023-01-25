﻿#define EMPTY
#define EMPTY_FN(a)
#define DEFINE #define
#define DEFINE_WITH_EMPTY EMPTY #define
#define DEFINE_NESTED(NAME, VALUE) \
	DEFINE NAME VALUE
#define DEFINE_NESTED_WITH_STATEMENT(NAME, VALUE) \
	var/test = 4;\
	var/test2 = 2;\
	DEFINE NAME VALUE

DEFINE A 1
DEFINE_WITH_EMPTY B 2
DEFINE_NESTED(C, 3)
EMPTY DEFINE D 4
 DEFINE E 5
DEFINE_NESTED_WITH_STATEMENT(F, 6)

/proc/RunTest()
	EMPTY
	EMPTY_FN(0)
	ASSERT(A == 1)
	ASSERT(B == 2)
	ASSERT(C == 3)
	ASSERT(D == 4)
	ASSERT(E == 5)
	ASSERT(F == 6)