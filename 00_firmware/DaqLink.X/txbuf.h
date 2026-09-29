/* 
 * File:   txbuf.h
 * Author: user
 *
 * Created on 2026年9月29日, 下午 4:46
 */

#ifndef TXBUF_H
#define	TXBUF_H

#include <stdint.h>

#ifdef	__cplusplus
extern "C" {
#endif

#define TXBUF_SIZE  512
    
    uint16_t txbuf_free(void);
    void txbuf_write(const uint8_t *src, uint16_t len);
    int txbuf_get(uint8_t *b);

#ifdef	__cplusplus
}
#endif

#endif	/* TXBUF_H */

