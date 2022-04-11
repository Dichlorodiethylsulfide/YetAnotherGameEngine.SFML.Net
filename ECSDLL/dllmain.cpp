// dllmain.cpp : Defines the entry point for the DLL application.
#include "pch.h"
#include <iostream>

BOOL APIENTRY DllMain(HMODULE hModule,
    DWORD  ul_reason_for_call,
    LPVOID lpReserved
)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}

extern "C"
{
    __declspec(dllexport) void* Malloc(int size)
    {
        return malloc(size);
    }

    __declspec(dllexport) void Free(void* ptr)
    {
        free(ptr);
    }

    __declspec(dllexport) void MemSet(void* ptr, int value, int size)
    {
        memset(ptr, value, size);
    }

    __declspec(dllexport) void MemMove(void* destination, void* source, int size)
    {
        memmove(destination, source, size);
    }
}