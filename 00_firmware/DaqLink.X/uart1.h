/* 
 * File:   uart1.h
 * Author: user
 *
 * Created on 2026年9月24日, 下午 4:12
 */

#ifndef UART1_H
#define	UART1_H

#include <stdint.h>

#ifdef	__cplusplus
extern "C" {
#endif

    void uart1_init(void);
    void uart1_putc(char c);
    void uart1_puts(const char *s);
    void uart1_put_uint(uint32_t v);
    int uart1_getc(void);
    
#ifdef	__cplusplus
}
#endif

#endif	/* UART1_H */

