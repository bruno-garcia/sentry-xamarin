#include "Sentry_Samples_Xamarin_Android_Ndk.h"
#include <android/log.h>

#define LOGI(...) ((void)__android_log_print(ANDROID_LOG_INFO, "Sentry_Samples_Xamarin_Android_Ndk", __VA_ARGS__))
#define LOGW(...) ((void)__android_log_print(ANDROID_LOG_WARN, "Sentry_Samples_Xamarin_Android_Ndk", __VA_ARGS__))

#define TAG "sentry-xamarin-sample"

extern "C" {
	/* This trivial function returns the platform ABI for which this dynamic native library is compiled.*/
	const char * Sentry_Samples_Xamarin_Android_Ndk::getPlatformABI()
	{
	#if defined(__arm__)
	#if defined(__ARM_ARCH_7A__)
	#if defined(__ARM_NEON__)
		#define ABI "armeabi-v7a/NEON"
	#else
		#define ABI "armeabi-v7a"
	#endif
	#else
		#define ABI "armeabi"
	#endif
	#elif defined(__i386__)
		#define ABI "x86"
	#else
		#define ABI "unknown"
	#endif
		LOGI("This dynamic shared library is compiled with ABI: %s", ABI);
		return "This native library is compiled with ABI: %s" ABI ".";
	}

	void Sentry_Samples_Xamarin_Android_Ndk()
	{
		__android_log_print(ANDROID_LOG_WARN, TAG, "About to crash.");
		char* ptr = 0;
		*ptr += 1;
	}

	Sentry_Samples_Xamarin_Android_Ndk::Sentry_Samples_Xamarin_Android_Ndk()
	{
	}

	Sentry_Samples_Xamarin_Android_Ndk::~Sentry_Samples_Xamarin_Android_Ndk()
	{
	}
}
