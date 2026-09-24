/* 
 * File:   spi2.h
 * Author: user
 *
 * Created on 2026年9月23日, 下午 9:37
 */

#ifndef SPI2_H
#define	SPI2_H

#include <stdint.h>

void spi2_init(void);
uint8_t spi2_xfer(uint8_t tx);

#ifdef	__cplusplus
extern "C" {
#endif




#ifdef	__cplusplus
}
#endif

#endif	/* SPI2_H */

