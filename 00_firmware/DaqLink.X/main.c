/*
 * File:   main.c
 * Author: user
 *
 * Created on 2026年9月23日, 下午 9:54
 */

#include "p32xxxx.h"
#include <sys/attribs.h>
#include "spi2.h"
#include "mcp4922.h"
#include "mcp3304.h"

int main(void)
{
    mcp4922_init();     /* 先把兩顆的 CS 拉高 */
    mcp3304_init();
    spi2_init();        /* 再啟動 SPI 匯流排 */

    while (1)
    {
    }
}
